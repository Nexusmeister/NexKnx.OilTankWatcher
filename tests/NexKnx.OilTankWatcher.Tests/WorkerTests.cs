using Microsoft.Extensions.Logging.Abstractions;
using NexKnx.OilTankWatcher.Configuration;
using NexKnx.OilTankWatcher.Knx;
using NexKnx.OilTankWatcher.Mqtt;
using NexKnx.OilTankWatcher.Persistence;
using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher.Tests;

/// <summary>
/// NSubstitute test doubles for the <see cref="Worker"/>. State that can't
/// be modeled with plain <c>Received()</c> checks (recorded writes, a
/// completion signal for asynchronous evaluations) is reproduced via thin
/// harness classes wrapped around the respective substitute.
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
    /// Signaled after every <c>WriteFaultAsync</c> call - the last step of
    /// the normal evaluation sequence before the optional remaining
    /// runtime. Lets tests deterministically wait for the completion of
    /// the fire-and-forget <c>EvaluateAndPublishAsync</c> instead of
    /// guessing with an arbitrary delay.
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

        // CA2012: NSubstitute configuration call, not an actual ValueTask
        // consumption - the call is intercepted by NSubstitute, not really executed.
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
        Exception? appendException = null,
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
            .Returns(_ =>
            {
                if (appendException is not null)
                {
                    throw appendException;
                }

                return Task.CompletedTask;
            });
        Repository.PruneAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // CA2012: NSubstitute configuration call, see KnxGatewayHarness above.
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
        // Kept small so PublishGate.ShouldPublish already returns true on
        // the very first call anyway (no _lastSentAt) - the values
        // themselves therefore aren't critical for most tests.
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

        // CA2012: NSubstitute configuration call, see KnxGatewayHarness above.
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
    /// <c>BackgroundService.StartAsync</c> returns as soon as
    /// <c>ExecuteAsync</c> has reached its first "await" - the MQTT
    /// subscription (<c>ValueReceived += ...</c>) may therefore not have
    /// happened yet. Instead of a guessed delay, this waits via retry
    /// until the MQTT substitute has actually started.
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

        (StartAsyncCallCount(mqtt) > 0).ShouldBeTrue("MqttOilLevelClient.StartAsync was not called within the timeout.");
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

        (await WaitForSignalAsync(knx.FaultWritten)).ShouldBeTrue("Evaluation did not complete within the timeout.");

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

        // First value establishes a baseline for the PlausibilityFilter.
        RaiseValue(mqtt, 50);
        (await WaitForSignalAsync(knx.FaultWritten)).ShouldBeTrue();
        knx.ResetFaultSignal();

        // Jump far beyond MaxStepPercent (10) - must be rejected.
        RaiseValue(mqtt, 95);

        // There's no success signal to wait on - a short, generously sized
        // wait ensures a (mistaken) evaluation would have time to show up.
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

        // The last persisted sample is only 5 minutes old - the freshly
        // received reading must therefore not trigger another daily-value
        // persistence operation.
        history.AppendedSamples.ShouldBeEmpty();

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LoadPersistedHistory_RepositoryThrows_DoesNotCrashStartup()
    {
        var mqtt = CreateMqttSubstitute();
        var knx = new KnxGatewayHarness();
        var history = new HistoryRepositoryHarness(
            initializeException: new InvalidOperationException("Database unreachable."));
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
            loadRecentException: new InvalidOperationException("History broken."));
        var worker = CreateWorker(mqtt, knx.Gateway, history.Repository);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ConnectKnx_Throws_IsSwallowed_ServiceKeepsRunning()
    {
        var mqtt = CreateMqttSubstitute();
        var knx = new KnxGatewayHarness(connectException: new InvalidOperationException("Bus unreachable."));
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
            // Declining series with enough points that RuntimeEstimator
            // (MinimumSamples = 3) would normally produce an estimate.
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

        // After StopAsync the handler was unsubscribed - another
        // "ValueReceived" call must no longer trigger any KNX writes.
        RaiseValue(mqtt, 42);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        knx.PercentWrites.ShouldBeEmpty();
    }

    [Fact]
    public async Task RuntimeEstimationEnabled_WithEnoughSamples_WritesRuntimeDays()
    {
        var mqtt = CreateMqttSubstitute();
        var knx = new KnxGatewayHarness();
        var now = DateTimeOffset.UtcNow;
        var history = new HistoryRepositoryHarness(
            // Declining series with enough points (MinimumSamples = 3) so
            // RuntimeEstimator produces a negative slope and thus an
            // estimate - the counterpart to RuntimeEstimationDisabled_...
            seededSamples:
            [
                new LevelSample(now.AddDays(-2), 80),
                new LevelSample(now.AddDays(-1), 70),
                new LevelSample(now, 60),
            ]);
        var worker = CreateWorker(mqtt, knx.Gateway, history.Repository);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        RaiseValue(mqtt, 50);
        (await WaitForSignalAsync(knx.FaultWritten)).ShouldBeTrue();

        knx.RuntimeDaysWrites.ShouldHaveSingleItem();

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NoSignificantChange_WithinSendInterval_SkipsSecondPublish()
    {
        var mqtt = CreateMqttSubstitute();
        var knx = new KnxGatewayHarness();
        var history = new HistoryRepositoryHarness();
        var worker = CreateWorker(mqtt, knx.Gateway, history.Repository);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        RaiseValue(mqtt, 60);
        (await WaitForSignalAsync(knx.FaultWritten)).ShouldBeTrue();
        knx.ResetFaultSignal();

        // Unchanged value within the send interval - PublishGate.ShouldPublish
        // must return false here, no second write may occur.
        RaiseValue(mqtt, 60);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        knx.PercentWrites.ShouldHaveSingleItem();

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PersistSample_RepositoryAppendThrows_DoesNotCrash()
    {
        var mqtt = CreateMqttSubstitute();
        var knx = new KnxGatewayHarness();
        var history = new HistoryRepositoryHarness(
            appendException: new InvalidOperationException("Write error in SQLite."));
        var worker = CreateWorker(mqtt, knx.Gateway, history.Repository);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        // First value always triggers a (here failing) persistence
        // operation - since _lastDailySampleAt is initially MinValue.
        RaiseValue(mqtt, 60);
        (await WaitForSignalAsync(knx.FaultWritten)).ShouldBeTrue();
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task EvaluateAndPublish_KnxWriteThrows_IsSwallowed_ServiceKeepsRunning()
    {
        var mqtt = CreateMqttSubstitute();
        var knx = new KnxGatewayHarness();
        var history = new HistoryRepositoryHarness();
        var worker = CreateWorker(mqtt, knx.Gateway, history.Repository);

        knx.Gateway.WritePercentAsync(Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("KNX write error.")));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilMqttStartedAsync(mqtt);

        RaiseValue(mqtt, 60);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // No FaultWritten signal, since WriteFaultAsync is no longer
        // reached at all because of the exception in WritePercentAsync -
        // what matters is that the error was caught and the service keeps running.
        knx.FaultWrites.ShouldBeEmpty();

        await worker.StopAsync(CancellationToken.None);
    }
}
