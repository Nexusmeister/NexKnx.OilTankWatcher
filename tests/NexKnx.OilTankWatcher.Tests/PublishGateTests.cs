using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher.Tests;

public class PublishGateTests
{
    [Fact]
    public void ShouldPublish_True_OnFirstEvaluation()
    {
        var gate = new PublishGate(TimeSpan.FromMinutes(10), changeThresholdPercent: 0.5);
        var now = DateTimeOffset.UtcNow;

        Assert.True(gate.ShouldPublish(now, 50, warning: false, fault: false));
    }

    [Fact]
    public void ShouldPublish_False_WhenNothingChangedAndIntervalNotElapsed()
    {
        var gate = new PublishGate(TimeSpan.FromMinutes(10), changeThresholdPercent: 0.5);
        var now = DateTimeOffset.UtcNow;
        gate.MarkSent(now, 50, warning: false, fault: false);

        var result = gate.ShouldPublish(now.AddMinutes(1), 50.1, warning: false, fault: false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldPublish_True_WhenChangeExceedsThreshold()
    {
        var gate = new PublishGate(TimeSpan.FromMinutes(10), changeThresholdPercent: 0.5);
        var now = DateTimeOffset.UtcNow;
        gate.MarkSent(now, 50, warning: false, fault: false);

        var result = gate.ShouldPublish(now.AddSeconds(1), 50.6, warning: false, fault: false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldPublish_True_WhenIntervalElapsedEvenWithoutChange()
    {
        var gate = new PublishGate(TimeSpan.FromMinutes(10), changeThresholdPercent: 0.5);
        var now = DateTimeOffset.UtcNow;
        gate.MarkSent(now, 50, warning: false, fault: false);

        var result = gate.ShouldPublish(now.AddMinutes(10), 50, warning: false, fault: false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldPublish_True_WhenWarningBitChanges()
    {
        var gate = new PublishGate(TimeSpan.FromMinutes(10), changeThresholdPercent: 0.5);
        var now = DateTimeOffset.UtcNow;
        gate.MarkSent(now, 50, warning: false, fault: false);

        var result = gate.ShouldPublish(now.AddSeconds(1), 50, warning: true, fault: false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldPublish_True_WhenFaultBitChanges()
    {
        var gate = new PublishGate(TimeSpan.FromMinutes(10), changeThresholdPercent: 0.5);
        var now = DateTimeOffset.UtcNow;
        gate.MarkSent(now, 50, warning: false, fault: false);

        var result = gate.ShouldPublish(now.AddSeconds(1), 50, warning: false, fault: true);

        Assert.True(result);
    }
}
