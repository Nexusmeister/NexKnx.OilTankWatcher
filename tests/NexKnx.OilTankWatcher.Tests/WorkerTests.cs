using Microsoft.Extensions.Logging.Abstractions;
using NexKnx.OilTankWatcher.Configuration;
using NexKnx.OilTankWatcher.Knx;
using NexKnx.OilTankWatcher.Mqtt;
using NexKnx.OilTankWatcher.Persistence;
using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher.Tests;

/// <summary>
/// Hand-geschriebene Test-Doubles für den <see cref="Worker"/>: kein Mocking-
/// Framework, damit die Tests sich am Stil von
/// <c>SqliteHistoryRepositoryTests</c> orientieren.
/// </summary>
public sealed class FakeMqttOilLevelClient : IMqttOilLevelClient
{
    public event Action<double>? ValueReceived;

    public int StartCalls { get; private set; }
    public int DisposeCalls { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        StartCalls++;
        return Task.CompletedTask;
    }

    public void Raise(double percent) => ValueReceived?.Invoke(percent);

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
    }
}

public sealed class FakeKnxGateway : IKnxGateway
{
    public int ConnectCalls { get; private set; }
    public int DisposeCalls { get; private set; }
    public Exception? ConnectException { get; set; }

    public List<double> PercentWrites { get; } = [];
    public List<double> LiterWrites { get; } = [];
    public List<bool> WarningWrites { get; } = [];
    public List<bool> FaultWrites { get; } = [];
    public List<double> RuntimeDaysWrites { get; } = [];

    /// <summary>
    /// Wird nach jedem <see cref="WriteFaultAsync"/>-Aufruf signalisiert -
    /// der letzte Schritt der normalen Auswertungssequenz vor der optionalen
    /// Restreichweite. Ermöglicht Tests, deterministisch auf den Abschluss
    /// des per "fire-and-forget" gestarteten <c>EvaluateAndPublishAsync</c>
    /// zu warten, statt mit einem beliebigen Delay zu raten.
    /// </summary>
    public TaskCompletionSource<bool> FaultWritten { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void ResetFaultSignal() => FaultWritten = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        ConnectCalls++;
        if (ConnectException is not null)
        {
            throw ConnectException;
        }

        return Task.CompletedTask;
    }

    public Task WritePercentAsync(double percent, CancellationToken cancellationToken)
    {
        PercentWrites.Add(percent);
        return Task.CompletedTask;
    }

    public Task WriteLitersAsync(double liters, CancellationToken cancellationToken)
    {
        LiterWrites.Add(liters);
        return Task.CompletedTask;
    }

    public Task WriteWarningAsync(bool warning, CancellationToken cancellationToken)
    {
        WarningWrites.Add(warning);
        return Task.CompletedTask;
    }

    public Task WriteFaultAsync(bool fault, CancellationToken cancellationToken)
    {
        FaultWrites.Add(fault);
        FaultWritten.TrySetResult(true);
        return Task.CompletedTask;
    }

    public Task WriteRuntimeDaysAsync(double days, CancellationToken cancellationToken)
    {
        RuntimeDaysWrites.Add(days);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
    }
}

public sealed class FakeHistoryRepository : IHistoryRepository
{
    public int InitializeCalls { get; private set; }
    public List<LevelSample> AppendedSamples { get; } = [];
    public int PruneCalls { get; private set; }
    public int DisposeCalls { get; private set; }

    public Exception? InitializeException { get; set; }
    public Exception? LoadRecentException { get; set; }
    public IReadOnlyList<LevelSample> SeededSamples { get; set; } = [];

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        InitializeCalls++;
        if (InitializeException is not null)
        {
            throw InitializeException;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LevelSample>> LoadRecentAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        if (LoadRecentException is not null)
        {
            throw LoadRecentException;
        }

        return Task.FromResult(SeededSamples);
    }

    public Task AppendAsync(LevelSample sample, CancellationToken cancellationToken)
    {
        AppendedSamples.Add(sample);
        return Task.CompletedTask;
    }

    public Task PruneAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        PruneCalls++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
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

    private static Worker CreateWorker(
        FakeMqttOilLevelClient mqtt,
        FakeKnxGateway knx,
        FakeHistoryRepository history,
        out TankOptions tankOptions,
        out KnxOptions knxOptions,
        RuntimeEstimationOptions? runtimeOptions = null)
    {
        tankOptions = CreateTankOptions();
        knxOptions = CreateKnxOptions();

        return new Worker(
            mqtt,
            knx,
            history,
            tankOptions,
            knxOptions,
            runtimeOptions ?? CreateRuntimeOptions(),
            NullLogger<Worker>.Instance);
    }

    private static async Task<bool> WaitForSignalAsync(TaskCompletionSource<bool> signal) =>
        await Task.WhenAny(signal.Task, Task.Delay(SignalTimeout)) == signal.Task;

    /// <summary>
    /// <c>BackgroundService.StartAsync</c> kehrt zurück, sobald
    /// <c>ExecuteAsync</c> am ersten "await" angelangt ist - das MQTT-Abo
    /// (<c>ValueReceived += ...</c>) kann daher noch nicht vollzogen sein.
    /// Statt eines geratenen Delays wird hier per Retry gewartet, bis der
    /// Fake-Client tatsächlich gestartet wurde.
    /// </summary>
    private static async Task WaitUntilMqttStartedAsync(FakeMqttOilLevelClient mqtt)
    {
        var deadline = DateTime.UtcNow + SignalTimeout;
        while (mqtt.StartCalls == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(mqtt.StartCalls > 0, "MqttOilLevelClient.StartAsync wurde nicht innerhalb des Timeouts aufgerufen.");
    }

    [Fact]
    public async Task ValidRawValue_FlowsThroughToKnxWrites()
    {
        var mqtt = new FakeMqttOilLevelClient();
        var knx = new FakeKnxGateway();
        var history = new FakeHistoryRepository();
        var worker = CreateWorker(mqtt, knx, history, out _, out _);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        mqtt.Raise(60);

        Assert.True(await WaitForSignalAsync(knx.FaultWritten), "Auswertung wurde nicht innerhalb des Timeouts abgeschlossen.");

        Assert.Single(knx.PercentWrites);
        Assert.Equal(60, knx.PercentWrites[0]);
        Assert.Single(knx.LiterWrites);
        Assert.Equal(600, knx.LiterWrites[0]);
        Assert.Single(knx.WarningWrites);
        Assert.False(knx.WarningWrites[0]);
        Assert.Single(knx.FaultWrites);
        Assert.False(knx.FaultWrites[0]);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ImplausibleJump_IsRejected_NoKnxWritesHappen()
    {
        var mqtt = new FakeMqttOilLevelClient();
        var knx = new FakeKnxGateway();
        var history = new FakeHistoryRepository();
        var worker = CreateWorker(mqtt, knx, history, out _, out _);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        // Erster Wert etabliert eine Baseline für den PlausibilityFilter.
        mqtt.Raise(50);
        Assert.True(await WaitForSignalAsync(knx.FaultWritten));
        knx.ResetFaultSignal();

        // Sprung weit über MaxStepPercent (10) hinaus - muss verworfen werden.
        mqtt.Raise(95);

        // Es gibt kein Erfolgs-Signal, auf das gewartet werden kann - eine
        // kurze, großzügig bemessene Wartezeit stellt sicher, dass eine
        // (fälschliche) Auswertung Zeit hätte, aufzulaufen.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Single(knx.PercentWrites);
        Assert.Equal(50, knx.PercentWrites[0]);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LoadPersistedHistory_SuppressesImmediateDuplicateDailySample()
    {
        var mqtt = new FakeMqttOilLevelClient();
        var knx = new FakeKnxGateway();
        var history = new FakeHistoryRepository
        {
            SeededSamples = [new LevelSample(DateTimeOffset.UtcNow.AddMinutes(-5), 55)],
        };
        var worker = CreateWorker(mqtt, knx, history, out _, out _);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        mqtt.Raise(55);
        Assert.True(await WaitForSignalAsync(knx.FaultWritten));

        // Der zuletzt persistierte Sample ist erst 5 Minuten alt - der
        // frisch empfangene Messwert darf daher keinen erneuten
        // Tageswert-Persistiervorgang auslösen.
        Assert.Empty(history.AppendedSamples);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LoadPersistedHistory_RepositoryThrows_DoesNotCrashStartup()
    {
        var mqtt = new FakeMqttOilLevelClient();
        var knx = new FakeKnxGateway();
        var history = new FakeHistoryRepository
        {
            InitializeException = new InvalidOperationException("Datenbank nicht erreichbar."),
        };
        var worker = CreateWorker(mqtt, knx, history, out _, out _);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LoadRecentAsync_Throws_DoesNotCrashStartup()
    {
        var mqtt = new FakeMqttOilLevelClient();
        var knx = new FakeKnxGateway();
        var history = new FakeHistoryRepository
        {
            LoadRecentException = new InvalidOperationException("Historie kaputt."),
        };
        var worker = CreateWorker(mqtt, knx, history, out _, out _);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ConnectKnx_Throws_IsSwallowed_ServiceKeepsRunning()
    {
        var mqtt = new FakeMqttOilLevelClient();
        var knx = new FakeKnxGateway
        {
            ConnectException = new InvalidOperationException("Bus nicht erreichbar."),
        };
        var history = new FakeHistoryRepository();
        var worker = CreateWorker(mqtt, knx, history, out _, out _);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        Assert.Equal(1, knx.ConnectCalls);

        mqtt.Raise(60);
        Assert.True(await WaitForSignalAsync(knx.FaultWritten));
        Assert.Single(knx.PercentWrites);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RuntimeEstimationDisabled_NeverWritesRuntimeDays()
    {
        var mqtt = new FakeMqttOilLevelClient();
        var knx = new FakeKnxGateway();
        var now = DateTimeOffset.UtcNow;
        var history = new FakeHistoryRepository
        {
            // Fallender Verlauf mit genügend Punkten, damit RuntimeEstimator
            // (MinimumSamples = 3) normalerweise eine Schätzung liefern würde.
            SeededSamples =
            [
                new LevelSample(now.AddDays(-2), 80),
                new LevelSample(now.AddDays(-1), 70),
                new LevelSample(now, 60),
            ],
        };
        var worker = CreateWorker(mqtt, knx, history, out _, out _, CreateRuntimeOptions(enabled: false));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        mqtt.Raise(50);
        Assert.True(await WaitForSignalAsync(knx.FaultWritten));

        Assert.Empty(knx.RuntimeDaysWrites);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_DisposesClientsAndUnsubscribes()
    {
        var mqtt = new FakeMqttOilLevelClient();
        var knx = new FakeKnxGateway();
        var history = new FakeHistoryRepository();
        var worker = CreateWorker(mqtt, knx, history, out _, out _);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, mqtt.DisposeCalls);
        Assert.Equal(1, knx.DisposeCalls);

        // Nach StopAsync wurde der Handler abgemeldet - ein weiterer
        // "ValueReceived"-Aufruf darf keine KNX-Schreibvorgänge mehr auslösen.
        mqtt.Raise(42);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Empty(knx.PercentWrites);
    }
}
