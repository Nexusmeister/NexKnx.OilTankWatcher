using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher.Tests;

public class PlausibilityFilterTests
{
    [Theory]
    [InlineData(-0.1)]
    [InlineData(100.1)]
    [InlineData(-50)]
    [InlineData(150)]
    public void TryAccept_RejectsValuesOutsideZeroToHundred(double raw)
    {
        var filter = new PlausibilityFilter(maxStepPercent: 5);

        var accepted = filter.TryAccept(raw, out var reason);

        accepted.ShouldBeFalse();
        reason.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void TryAccept_AcceptsFirstValueInRange(double raw)
    {
        var filter = new PlausibilityFilter(maxStepPercent: 5);

        var accepted = filter.TryAccept(raw, out var reason);

        accepted.ShouldBeTrue();
        reason.ShouldBeNull();
        filter.LastAccepted.ShouldBe(raw);
    }

    [Fact]
    public void TryAccept_AcceptsStepExactlyAtMaximum()
    {
        var filter = new PlausibilityFilter(maxStepPercent: 5);
        filter.TryAccept(50, out _);

        var accepted = filter.TryAccept(55, out var reason);

        accepted.ShouldBeTrue();
        reason.ShouldBeNull();
        filter.LastAccepted.ShouldBe(55);
    }

    [Fact]
    public void TryAccept_RejectsStepBeyondMaximum()
    {
        var filter = new PlausibilityFilter(maxStepPercent: 5);
        filter.TryAccept(50, out _);

        var accepted = filter.TryAccept(55.1, out var reason);

        accepted.ShouldBeFalse();
        reason.ShouldNotBeNull();
        // Last accepted value stays unchanged - the outlier must not
        // corrupt the reference for future checks.
        filter.LastAccepted.ShouldBe(50);
    }

    [Fact]
    public void TryAccept_RejectsSuddenDropAsWellAsSuddenRise()
    {
        var filter = new PlausibilityFilter(maxStepPercent: 5);
        filter.TryAccept(50, out _);

        var accepted = filter.TryAccept(40, out var reason);

        accepted.ShouldBeFalse();
        reason.ShouldNotBeNull();
    }

    [Fact]
    public void TryAccept_AllowsSlowContinuousDecline()
    {
        var filter = new PlausibilityFilter(maxStepPercent: 5);

        filter.TryAccept(80, out _).ShouldBeTrue();
        filter.TryAccept(76, out _).ShouldBeTrue();
        filter.TryAccept(72, out _).ShouldBeTrue();
        filter.TryAccept(68, out _).ShouldBeTrue();

        filter.LastAccepted.ShouldBe(68);
    }

    [Fact]
    public void Constructor_ThrowsForNonPositiveMaxStep()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new PlausibilityFilter(0));
        Should.Throw<ArgumentOutOfRangeException>(() => new PlausibilityFilter(-1));
    }
}
