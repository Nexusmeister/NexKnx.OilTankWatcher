using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher.Tests;

public class HysteresisGateTests
{
    [Fact]
    public void Update_StaysInactive_AboveActivationThreshold()
    {
        var gate = new HysteresisGate(activateAtOrBelow: 25, releaseAbove: 28);

        var result = gate.Update(30);

        result.ShouldBeFalse();
        gate.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Update_Activates_AtOrBelowThreshold()
    {
        var gate = new HysteresisGate(activateAtOrBelow: 25, releaseAbove: 28);

        var result = gate.Update(25);

        result.ShouldBeTrue();
        gate.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Update_StaysActive_InsideHysteresisBand()
    {
        var gate = new HysteresisGate(activateAtOrBelow: 25, releaseAbove: 28);
        gate.Update(20); // activated

        // 26% is between the thresholds - the warning must not be cleared yet.
        var result = gate.Update(26);

        result.ShouldBeTrue();
    }

    [Fact]
    public void Update_Releases_StrictlyAboveRecoveryThreshold()
    {
        var gate = new HysteresisGate(activateAtOrBelow: 25, releaseAbove: 28);
        gate.Update(20); // activated

        var stillActive = gate.Update(28); // exactly at the threshold - not yet cleared
        var released = gate.Update(28.1);

        stillActive.ShouldBeTrue();
        released.ShouldBeFalse();
    }

    [Fact]
    public void Update_DoesNotFlicker_AroundActivationThresholdAlone()
    {
        var gate = new HysteresisGate(activateAtOrBelow: 25, releaseAbove: 28);

        gate.Update(24); // activated
        var afterSmallRise = gate.Update(26); // above the activation threshold, but below the recovery threshold

        afterSmallRise.ShouldBeTrue("Without hysteresis this would immediately deactivate again - that's undesired.");
    }

    [Fact]
    public void Constructor_ThrowsWhenReleaseThresholdNotAboveActivationThreshold()
    {
        Should.Throw<ArgumentException>(() => new HysteresisGate(activateAtOrBelow: 25, releaseAbove: 25));
        Should.Throw<ArgumentException>(() => new HysteresisGate(activateAtOrBelow: 25, releaseAbove: 20));
    }
}
