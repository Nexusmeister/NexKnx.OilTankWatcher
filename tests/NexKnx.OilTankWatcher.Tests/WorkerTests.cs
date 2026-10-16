using Microsoft.Extensions.Logging.Abstractions;
using NexKnx.OilTankWatcher.Configuration;
using NexKnx.OilTankWatcher.Knx;
using NexKnx.OilTankWatcher.Mqtt;
using NexKnx.OilTankWatcher.Persistence;
using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher.Tests;

/// <summary>
/// NSubstitute-Test-Doubles für den <see cref="Worker"/>. Zustand, der sich
/// mit reinen <c>Received()</c>-Prüfungen nicht abbilden lässt (aufgezeichnete
/// Schreibvorgänge, ein Fertigstellungs-Signal für asynchrone Auswertungen),
/// wird über schlanke Harness-Klassen um den jeweiligen Substitute herum
/// nachgebildet.
/// </summary>
file sealed class KnxGatewayHarness
{
    public IKnxGateway Gateway { get; }

    public List<double> PercentWrites { get; } = [];
    public List<double> LiterWrites { get; } = [];
    public List<bool> WarningWrites { get; } = [];
    public List<bool> FaultWrites { get; } = [];
    public List<double> RuntimeDaysWrites { get; } = [];

    /// <summary>
    /// Wird nach jedem <c>WriteFaultAsync</c>-Aufruf signalisiert - der
    /// letzte Schritt der normalen Auswertungssequenz vor der optionalen
    /// Restreichweite. Ermöglicht Tests, deterministisch auf den Abschluss
    /// des per "fire-and-forget" gestarteten <c>EvaluateAndPublishAsync</c>
    /// zu warten, statt mit einem beliebigen Delay zu raten.
    /// </summary>
    public TaskCompletionSource<bool> FaultWritten { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public KnxGatewayHarness(Exception? connectException = null)
    {
        Gateway = Substitute.For<IKnxGateway>();

        Gateway.ConnectAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (connectException is not null)
            {
                throw connectException;
            }

            return Task.CompletedTask;
        });

        Gateway.WritePercentAsync(Arg.Do<double>(p => PercentWrites.Add(p)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        Gateway.WriteLitersAsync(Arg.Do<double>(l => LiterWrites.Add(l)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        Gateway.WriteWarningAsync(Arg.Do<bool>(w => WarningWrites.Add(w)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        Gateway.WriteFaultAsync(Arg.Do<bool>(f => FaultWrites.Add(f)), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                FaultWritten.TrySetResult(true);
                return Task.CompletedTask;
            });
        Gateway.WriteRuntimeDaysAsync(Arg.Do<double>(d => RuntimeDaysWrites.Add(d)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // CA2012: NSubstitute-Konfigurationsaufruf, kein tatsaechlicher
        // ValueTask-Verbrauch - der Aufruf wird von NSubstitute abgefangen,
        // nicht wirklich ausgefuehrt.
#pragma warning disable CA2012
        Gateway.DisposeAsync().Returns(ValueTask.CompletedTask);
#pragma warning restore CA2012
    }

    public void ResetFaultSignal() => FaultWritten = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

file sealed class HistoryRepositoryHarness
{
    public IHistoryRepository Repository { get; }

    public List<LevelSample> AppendedSamples { get; } = [];

    public HistoryRepositoryHarness(
        Exception? initializeException = null,
        Exception? loadRecentException = null,
        IReadOnlyList<LevelSample>? seededSamples = null)
    {
        Repository = Substitute.For<IHistoryRepository>();

        Repository.InitializeAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (initializeException is not null)
            {
                throw initializeException;
            }

            return Task.CompletedTask;
        });

        Repository.LoadRecentAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (loadRecentException is not null)
            {
                throw loadRecentException;
            }

            return Task.FromResult(seededSamples ?? (IReadOnlyList<LevelSample>)[]);
        });

        Repository.AppendAsync(Arg.Do<LevelSample>(s => AppendedSamples.Add(s)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        Repository.PruneAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // CA2012: NSubstitute-Konfigurationsaufruf, siehe KnxGatewayHarness oben.
#pragma warning disable CA2012
        Repository.DisposeAsync().Returns(ValueTask.CompletedTask);
#pragma warning restore CA2012
    }
}

public sealed class WorkerTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);

    private static TankOptions CreateTankOptions() => new()
    {
        NominalVolumeLiters = 1000,
        MaxStepPercent = 10,
        MedianWindowSize = 1,
    };

    private static KnxOptions CreateKnxOptions() => new()
    {
        WarningThresholdPercent = 25,
        WarningRecoveryPercent = 28,
        FaultAfterHours = 12,
        // Klein gehalten, damit PublishGate.ShouldPublish beim allerersten
        // Aufruf (kein _lastSentAt) ohnehin schon true liefert - die Werte
        // selbst sind daher für die meisten Tests nicht kritisch.
        SendIntervalMinutes = 1,
        ChangeThresholdPercent = 0.1,
    };

    private static RuntimeEstimationOptions CreateRuntimeOptions(bool enabled = true) => new()
    {
        Enabled = enabled,
        HistoryDays = 30,
        MinimumSamples = 3,
    };

    private static IMqttOilLevelClient CreateMqttSubstitute()
    {
        var mqtt = Substitute.For<IMqttOilLevelClient>();
        mqtt.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // CA2012: NSubstitute-Konfigurationsaufruf, siehe KnxGatewayHarness oben.
#pragma warning disable CA2012
        mqtt.DisposeAsync().Returns(ValueTask.CompletedTask);
#pragma warning restore CA2012
        return mqtt;
    }

    private static void RaiseValue(IMqttOilLevelClient mqtt, double percent) =>
        mqtt.ValueReceived += Raise.Event<Action<double>>(percent);

    private static Worker CreateWorker(
        IMqttOilLevelClient mqtt,
        IKnxGateway knx,
        IHistoryRepository history,
        RuntimeEstimationOptions? runtimeOptions = null) =>
        new(
            mqtt,
            knx,
            history,
            CreateTankOptions(),
            CreateKnxOptions(),
            runtimeOptions ?? CreateRuntimeOptions(),
            NullLogger<Worker>.Instance);

    private static async Task<bool> WaitForSignalAsync(TaskCompletionSource<bool> signal) =>
        await Task.WhenAny(signal.Task, Task.Delay(SignalTimeout)) == signal.Task;

    /// <summary>
    /// <c>BackgroundService.StartAsync</c> kehrt zurück, sobald
    /// <c>ExecuteAsync</c> am ersten "await" angelangt ist - das MQTT-Abo
    /// (<c>ValueReceived += ...</c>) kann daher noch nicht vollzogen sein.
    /// Statt eines geratenen Delays wird hier per Retry gewartet, bis der
    /// Mqtt-Substitute tatsächlich gestartet wurde.
    /// </summary>
    private static int StartAsyncCallCount(IMqttOilLevelClient mqtt) =>
        mqtt.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IMqttOilLevelClient.StartAsync));

    private static async Task WaitUntilMqttStartedAsync(IMqttOilLevelClient mqtt)
    {
        var deadline = DateTime.UtcNow + SignalTimeout;
        while (StartAsyncCallCount(mqtt) == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        (StartAsyncCallCount(mqtt) > 0).ShouldBeTrue("MqttOilLevelClient.StartAsync wurde nicht innerhalb des Timeouts aufgerufen.");
    }

    [Fact]
    public async Task ValidRawValue_FlowsThroughToKnxWrites()
    {
        var mqtt = CreateMqttSubstitute();
        var knx = new KnxGatewayHarness();
        var history = new HistoryRepositoryHarness();
        var worker = CreateWorker(mqtt, knx.Gateway, history.Repository);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        RaiseValue(mqtt, 60);

        (await WaitForSignalAsync(knx.FaultWritten)).ShouldBeTrue("Auswertung wurde nicht innerhalb des Timeouts abgeschlossen.");

        knx.PercentWrites.ShouldHaveSingleItem();
        knx.PercentWrites[0].ShouldBe(60);
        knx.LiterWrites.ShouldHaveSingleItem();
        knx.LiterWrites[0].ShouldBe(600);
        knx.WarningWrites.ShouldHaveSingleItem();
        knx.WarningWrites[0].ShouldBeFalse();
        knx.FaultWrites.ShouldHaveSingleItem();
        knx.FaultWrites[0].ShouldBeFalse();

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ImplausibleJump_IsRejected_NoKnxWritesHappen()
    {
        var mqtt = CreateMqttSubstitute();
        var knx = new KnxGatewayHarness();
        var history = new HistoryRepositoryHarness();
        var worker = CreateWorker(mqtt, knx.Gateway, history.Repository);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        // Erster Wert etabliert eine Baseline für den PlausibilityFilter.
        RaiseValue(mqtt, 50);
        (await WaitForSignalAsync(knx.FaultWritten)).ShouldBeTrue();
        knx.ResetFaultSignal();

        // Sprung weit über MaxStepPercent (10) hinaus - muss verworfen werden.
        RaiseValue(mqtt, 95);

        // Es gibt kein Erfolgs-Signal, auf das gewartet werden kann - eine
        // kurze, großzügig bemessene Wartezeit stellt sicher, dass eine
        // (fälschliche) Auswertung Zeit hätte, aufzulaufen.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        knx.PercentWrites.ShouldHaveSingleItem();
        knx.PercentWrites[0].ShouldBe(50);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LoadPersistedHistory_SuppressesImmediateDuplicateDailySample()
    {
        var mqtt = CreateMqttSubstitute();
        var knx = new KnxGatewayHarness();
        var history = new HistoryRepositoryHarness(
            seededSamples: [new LevelSample(DateTimeOffset.UtcNow.AddMinutes(-5), 55)]);
        var worker = CreateWorker(mqtt, knx.Gateway, history.Repository);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        RaiseValue(mqtt, 55);
        (await WaitForSignalAsync(knx.FaultWritten)).ShouldBeTrue();

        // Der zuletzt persistierte Sample ist erst 5 Minuten alt - der
        // frisch empfangene Messwert darf daher keinen erneuten
        // Tageswert-Persistiervorgang auslösen.
        history.AppendedSamples.ShouldBeEmpty();

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LoadPersistedHistory_RepositoryThrows_DoesNotCrashStartup()
    {
        var mqtt = CreateMqttSubstitute();
        var knx = new KnxGatewayHarness();
        var history = new HistoryRepositoryHarness(
            initializeException: new InvalidOperationException("Datenbank nicht erreichbar."));
        var worker = CreateWorker(mqtt, knx.Gateway, history.Repository);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LoadRecentAsync_Throws_DoesNotCrashStartup()
    {
        var mqtt = CreateMqttSubstitute();
        var knx = new KnxGatewayHarness();
        var history = new HistoryRepositoryHarness(
            loadRecentException: new InvalidOperationException("Historie kaputt."));
        var worker = CreateWorker(mqtt, knx.Gateway, history.Repository);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ConnectKnx_Throws_IsSwallowed_ServiceKeepsRunning()
    {
        var mqtt = CreateMqttSubstitute();
        var knx = new KnxGatewayHarness(connectException: new InvalidOperationException("Bus nicht erreichbar."));
        var history = new HistoryRepositoryHarness();
        var worker = CreateWorker(mqtt, knx.Gateway, history.Repository);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        await knx.Gateway.Received(1).ConnectAsync(Arg.Any<CancellationToken>());

        RaiseValue(mqtt, 60);
        (await WaitForSignalAsync(knx.FaultWritten)).ShouldBeTrue();
        knx.PercentWrites.ShouldHaveSingleItem();

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RuntimeEstimationDisabled_NeverWritesRuntimeDays()
    {
        var mqtt = CreateMqttSubstitute();
        var knx = new KnxGatewayHarness();
        var now = DateTimeOffset.UtcNow;
        var history = new HistoryRepositoryHarness(
            // Fallender Verlauf mit genügend Punkten, damit RuntimeEstimator
            // (MinimumSamples = 3) normalerweise eine Schätzung liefern würde.
            seededSamples:
            [
                new LevelSample(now.AddDays(-2), 80),
                new LevelSample(now.AddDays(-1), 70),
                new LevelSample(now, 60),
            ]);
        var worker = CreateWorker(mqtt, knx.Gateway, history.Repository, CreateRuntimeOptions(enabled: false));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        RaiseValue(mqtt, 50);
        (await WaitForSignalAsync(knx.FaultWritten)).ShouldBeTrue();

        knx.RuntimeDaysWrites.ShouldBeEmpty();

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_DisposesClientsAndUnsubscribes()
    {
        var mqtt = CreateMqttSubstitute();
        var knx = new KnxGatewayHarness();
        var history = new HistoryRepositoryHarness();
        var worker = CreateWorker(mqtt, knx.Gateway, history.Repository);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        await worker.StopAsync(CancellationToken.None);

        await mqtt.Received(1).DisposeAsync();
        await knx.Gateway.Received(1).DisposeAsync();

        // Nach StopAsync wurde der Handler abgemeldet - ein weiterer
        // "ValueReceived"-Aufruf darf keine KNX-Schreibvorgänge mehr auslösen.
        RaiseValue(mqtt, 42);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        knx.PercentWrites.ShouldBeEmpty();
    }
}
