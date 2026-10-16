using System.Diagnostics.Metrics;
using NexKnx.OilTankWatcher.Configuration;
using NexKnx.OilTankWatcher.Knx;
using NexKnx.OilTankWatcher.Logging;
using NexKnx.OilTankWatcher.Mqtt;
using NexKnx.OilTankWatcher.Persistence;
using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher;

/// <summary>
/// Orchestrates the data flow MQTT -> plausibility check -> smoothing ->
/// hysteresis/fault detection -> KNX. New readings immediately trigger an
/// evaluation; a 1-minute timer additionally takes care of the cyclic
/// evaluation (fault timeout, periodic resend), even when no new MQTT
/// messages are currently arriving.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly IMqttOilLevelClient _mqttClient;
    private readonly IKnxGateway _knxGateway;
    private readonly IHistoryRepository _historyRepository;
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
    private double? _currentRawPercent;
    private DateTimeOffset _lastDailySampleAt = DateTimeOffset.MinValue;

    // Made observable for Grafana/Prometheus - the actual bits otherwise
    // only go onto the KNX bus and can't be queried from the outside.
    private readonly Meter _meter = new("NexKnx.OilTankWatcher");
    private int _warningActive;
    private int _faultActive;

    public Worker(
        IMqttOilLevelClient mqttClient,
        IKnxGateway knxGateway,
        IHistoryRepository historyRepository,
        TankOptions tankOptions,
        KnxOptions knxOptions,
        RuntimeEstimationOptions runtimeOptions,
        ILogger<Worker> logger)
    {
        _mqttClient = mqttClient;
        _knxGateway = knxGateway;
        _historyRepository = historyRepository;
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

        _meter.CreateObservableGauge(
            "oiltank_warning_active",
            () => Volatile.Read(ref _warningActive),
            description: "1 if the warning threshold is currently undercut, otherwise 0.");
        _meter.CreateObservableGauge(
            "oiltank_fault_active",
            () => Volatile.Read(ref _faultActive),
            description: "1 if a fault (no valid reading for too long) is currently present, otherwise 0.");
        _meter.CreateObservableGauge(
            "oiltank_level_percent",
            GetCurrentSmoothedPercentMeasurements,
            unit: "%",
            description: "Current filtered/smoothed fill level in percent.");
    }

    private IEnumerable<Measurement<double>> GetCurrentSmoothedPercentMeasurements()
    {
        double? smoothed;
        lock (_stateLock)
        {
            smoothed = _currentSmoothedPercent;
        }

        // No valid reading since the service started yet - report no
        // measurement instead of a misleading 0% value.
        if (smoothed is null)
        {
            yield break;
        }

        yield return new Measurement<double>(smoothed.Value);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadPersistedHistoryAsync(stoppingToken);
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
            // Normal shutdown via the shutdown token.
        }
    }

    private async Task ConnectKnxAsync(CancellationToken cancellationToken)
    {
        // Only ONE initial connection attempt: ConnectorParameters.AutoReconnect
        // (see FalconKnxGateway) takes over all further connection and
        // reconnection attempts internally in the background. A separate
        // manual retry loop around ConnectAsync collides with that - after a
        // failed first attempt the bus already switches internally into a
        // "Connecting" state, a second manual ConnectAsync call then throws
        // "InvalidOperationException: Bus object already connected"
        // (reproduced for real in a Docker test).
        try
        {
            await _knxGateway.ConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Initial KNX connection failed. AutoReconnect will take over further attempts in the background.");
        }
    }

    private async Task LoadPersistedHistoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _historyRepository.InitializeAsync(cancellationToken);

            var retention = TimeSpan.FromDays(_runtimeOptions.HistoryDays);
            var persisted = await _historyRepository.LoadRecentAsync(retention, cancellationToken);

            foreach (var sample in persisted)
            {
                _history.Add(sample);
            }

            if (persisted.Count > 0)
            {
                // Prevents a new daily value from being recorded again right
                // after a restart, when the last persisted value isn't yet
                // 24h old.
                _lastDailySampleAt = persisted[^1].Timestamp;
            }

            _logger.LogHistoryLoaded(persisted.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Persisted history could not be loaded - starting with an empty history.");
        }
    }

    private async Task PersistSampleAsync(LevelSample sample)
    {
        try
        {
            await _historyRepository.AppendAsync(sample, CancellationToken.None);
            await _historyRepository.PruneAsync(TimeSpan.FromDays(_runtimeOptions.HistoryDays), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "History value could not be persisted to SQLite.");
        }
    }

    private void OnRawValueReceived(double rawPercent)
    {
        if (!_plausibilityFilter.TryAccept(rawPercent, out var rejectReason))
        {
            _logger.LogWarning("Reading rejected: {Reason}", rejectReason);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var smoothed = _medianSmoother.Add(rawPercent);
        _faultDetector.MarkValid(now);

        lock (_stateLock)
        {
            _currentSmoothedPercent = smoothed;
            _currentRawPercent = rawPercent;
        }

        if (now - _lastDailySampleAt >= TimeSpan.FromHours(24))
        {
            var sample = new LevelSample(now, smoothed);
            _history.Add(sample);
            _lastDailySampleAt = now;
            _ = PersistSampleAsync(sample);
        }

        // Immediate evaluation on a new reading, don't wait for the minute timer.
        _ = EvaluateAndPublishAsync(CancellationToken.None);
    }

    private async Task EvaluateAndPublishAsync(CancellationToken cancellationToken)
    {
        await _publishLock.WaitAsync(CancellationToken.None);
        try
        {
            double? smoothed;
            double? rawPercent;
            lock (_stateLock)
            {
                smoothed = _currentSmoothedPercent;
                rawPercent = _currentRawPercent;
            }

            var now = DateTimeOffset.UtcNow;
            var fault = _faultDetector.IsFaulty(now);
            Volatile.Write(ref _faultActive, fault ? 1 : 0);

            if (smoothed is null)
            {
                // No valid reading since the service started yet.
                return;
            }

            var percent = smoothed.Value;
            var warning = _warningGate.Update(percent);
            Volatile.Write(ref _warningActive, warning ? 1 : 0);

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

            _logger.LogKnxUpdated(percent, liters, rawPercent ?? percent, warning, fault);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during evaluation/publishing.");
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
