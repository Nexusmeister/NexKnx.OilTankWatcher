using NexKnx.OilTankWatcher.Configuration;
using NexKnx.OilTankWatcher.Knx;
using NexKnx.OilTankWatcher.Mqtt;
using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher;

/// <summary>
/// Orchestriert den Datenfluss MQTT -> Plausibilitätsprüfung -> Glättung ->
/// Hysterese/Störungserkennung -> KNX. Neue Messwerte lösen sofort eine
/// Auswertung aus; ein 1-Minuten-Timer sorgt zusätzlich für die zyklische
/// Auswertung (Störungs-Timeout, periodisches Resend), auch wenn gerade
/// keine neuen MQTT-Nachrichten eintreffen.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly MqttOilLevelClient _mqttClient;
    private readonly IKnxGateway _knxGateway;
    private readonly TankOptions _tankOptions;
    private readonly KnxOptions _knxOptions;
    private readonly RuntimeEstimationOptions _runtimeOptions;
    private readonly ILogger<Worker> _logger;

    private readonly PlausibilityFilter _plausibilityFilter;
    private readonly MedianSmoother _medianSmoother;
    private readonly HysteresisGate _warningGate;
    private readonly FaultDetector _faultDetector;
    private readonly ReadingHistory _history;
    private readonly PublishGate _publishGate;
    private readonly SemaphoreSlim _publishLock = new(1, 1);

    private readonly object _stateLock = new();
    private double? _currentSmoothedPercent;
    private DateTimeOffset _lastDailySampleAt = DateTimeOffset.MinValue;

    public Worker(
        MqttOilLevelClient mqttClient,
        IKnxGateway knxGateway,
        TankOptions tankOptions,
        KnxOptions knxOptions,
        RuntimeEstimationOptions runtimeOptions,
        ILogger<Worker> logger)
    {
        _mqttClient = mqttClient;
        _knxGateway = knxGateway;
        _tankOptions = tankOptions;
        _knxOptions = knxOptions;
        _runtimeOptions = runtimeOptions;
        _logger = logger;

        _plausibilityFilter = new PlausibilityFilter(_tankOptions.MaxStepPercent);
        _medianSmoother = new MedianSmoother(_tankOptions.MedianWindowSize);
        _warningGate = new HysteresisGate(_knxOptions.WarningThresholdPercent, _knxOptions.WarningRecoveryPercent);
        _faultDetector = new FaultDetector(TimeSpan.FromHours(_knxOptions.FaultAfterHours), DateTimeOffset.UtcNow);
        _history = new ReadingHistory(TimeSpan.FromDays(_runtimeOptions.HistoryDays));
        _publishGate = new PublishGate(TimeSpan.FromMinutes(_knxOptions.SendIntervalMinutes), _knxOptions.ChangeThresholdPercent);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ConnectKnxAsync(stoppingToken);

        _mqttClient.ValueReceived += OnRawValueReceived;
        await _mqttClient.StartAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await EvaluateAndPublishAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normales Beenden über den Shutdown-Token.
        }
    }

    private async Task ConnectKnxAsync(CancellationToken cancellationToken)
    {
        // Nur EIN initialer Verbindungsversuch: ConnectorParameters.AutoReconnect
        // (siehe FalconKnxGateway) übernimmt alle weiteren Verbindungs- und
        // Wiederverbindungsversuche intern im Hintergrund. Ein eigener
        // manueller Retry-Loop um ConnectAsync kollidiert damit - der Bus
        // wechselt nach einem fehlgeschlagenen ersten Versuch bereits intern
        // in einen "Connecting"-Zustand, ein zweiter manueller ConnectAsync-
        // Aufruf wirft dann "InvalidOperationException: Bus object already
        // connected" (im Docker-Test real reproduziert).
        try
        {
            await _knxGateway.ConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Erste KNX-Verbindung fehlgeschlagen. AutoReconnect übernimmt weitere Versuche im Hintergrund.");
        }
    }

    private void OnRawValueReceived(double rawPercent)
    {
        if (!_plausibilityFilter.TryAccept(rawPercent, out var rejectReason))
        {
            _logger.LogWarning("Messwert verworfen: {Reason}", rejectReason);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var smoothed = _medianSmoother.Add(rawPercent);
        _faultDetector.MarkValid(now);

        lock (_stateLock)
        {
            _currentSmoothedPercent = smoothed;
        }

        if (now - _lastDailySampleAt >= TimeSpan.FromHours(24))
        {
            _history.Add(new LevelSample(now, smoothed));
            _lastDailySampleAt = now;
        }

        // Sofort-Auswertung bei neuem Messwert, nicht auf den Minuten-Timer warten.
        _ = EvaluateAndPublishAsync(CancellationToken.None);
    }

    private async Task EvaluateAndPublishAsync(CancellationToken cancellationToken)
    {
        await _publishLock.WaitAsync(CancellationToken.None);
        try
        {
            double? smoothed;
            lock (_stateLock)
            {
                smoothed = _currentSmoothedPercent;
            }

            var now = DateTimeOffset.UtcNow;
            var fault = _faultDetector.IsFaulty(now);

            if (smoothed is null)
            {
                // Noch kein gültiger Messwert seit Start des Dienstes.
                return;
            }

            var percent = smoothed.Value;
            var warning = _warningGate.Update(percent);

            if (!_publishGate.ShouldPublish(now, percent, warning, fault))
            {
                return;
            }

            var liters = percent / 100.0 * _tankOptions.NominalVolumeLiters;

            await _knxGateway.WritePercentAsync(percent, cancellationToken);
            await _knxGateway.WriteLitersAsync(liters, cancellationToken);
            await _knxGateway.WriteWarningAsync(warning, cancellationToken);
            await _knxGateway.WriteFaultAsync(fault, cancellationToken);

            if (_runtimeOptions.Enabled)
            {
                var days = RuntimeEstimator.EstimateDaysRemaining(_history.Samples, _runtimeOptions.MinimumSamples);
                if (days is not null)
                {
                    await _knxGateway.WriteRuntimeDaysAsync(days.Value, cancellationToken);
                }
            }

            _publishGate.MarkSent(now, percent, warning, fault);

            _logger.LogInformation(
                "KNX aktualisiert: {Percent:0.0}% ({Liters:0} l), Warnung={Warning}, Störung={Fault}.",
                percent,
                liters,
                warning,
                fault);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unerwarteter Fehler bei der Auswertung/Veröffentlichung.");
        }
        finally
        {
            _publishLock.Release();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        _mqttClient.ValueReceived -= OnRawValueReceived;
        await _mqttClient.DisposeAsync();
        await _knxGateway.DisposeAsync();
    }
}
