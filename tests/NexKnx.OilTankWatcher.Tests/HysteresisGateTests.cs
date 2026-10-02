using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher.Tests;

public class HysteresisGateTests
{
    [Fact]
    public void Update_StaysInactive_AboveActivationThreshold()
    {
        var gate = new HysteresisGate(activateAtOrBelow: 25, releaseAbove: 28);

        var result = gate.Update(30);

        Assert.False(result);
        Assert.False(gate.IsActive);
    }

    [Fact]
    public void Update_Activates_AtOrBelowThreshold()
    {
        var gate = new HysteresisGate(activateAtOrBelow: 25, releaseAbove: 28);

        var result = gate.Update(25);

        Assert.True(result);
        Assert.True(gate.IsActive);
    }

    [Fact]
    public void Update_StaysActive_InsideHysteresisBand()
    {
        var gate = new HysteresisGate(activateAtOrBelow: 25, releaseAbove: 28);
        gate.Update(20); // aktiviert

        // 26% liegt zwischen den Schwellen - Warnung darf noch nicht zurückgenommen werden.
        var result = gate.Update(26);

        Assert.True(result);
    }

    [Fact]
    public void Update_Releases_StrictlyAboveRecoveryThreshold()
    {
        var gate = new HysteresisGate(activateAtOrBelow: 25, releaseAbove: 28);
        gate.Update(20); // aktiviert

        var stillActive = gate.Update(28); // genau auf der Schwelle - noch nicht zurückgenommen
        var released = gate.Update(28.1);

        Assert.True(stillActive);
        Assert.False(released);
    }

    [Fact]
    public void Update_DoesNotFlicker_AroundActivationThresholdAlone()
    {
        var gate = new HysteresisGate(activateAtOrBelow: 25, releaseAbove: 28);

        gate.Update(24); // aktiviert
        var afterSmallRise = gate.Update(26); // über Aktivierungsschwelle, aber unter Rücknahmeschwelle

        Assert.True(afterSmallRise, "Ohne Hysterese würde hier sofort wieder deaktiviert - das ist unerwünscht.");
    }

    [Fact]
    public void Constructor_ThrowsWhenReleaseThresholdNotAboveActivationThreshold()
    {
        Assert.Throws<ArgumentException>(() => new HysteresisGate(activateAtOrBelow: 25, releaseAbove: 25));
        Assert.Throws<ArgumentException>(() => new HysteresisGate(activateAtOrBelow: 25, releaseAbove: 20));
    }
}
