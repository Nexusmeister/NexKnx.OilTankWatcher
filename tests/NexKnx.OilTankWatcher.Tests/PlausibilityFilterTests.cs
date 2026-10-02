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

        Assert.False(accepted);
        Assert.NotNull(reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void TryAccept_AcceptsFirstValueInRange(double raw)
    {
        var filter = new PlausibilityFilter(maxStepPercent: 5);

        var accepted = filter.TryAccept(raw, out var reason);

        Assert.True(accepted);
        Assert.Null(reason);
        Assert.Equal(raw, filter.LastAccepted);
    }

    [Fact]
    public void TryAccept_AcceptsStepExactlyAtMaximum()
    {
        var filter = new PlausibilityFilter(maxStepPercent: 5);
        filter.TryAccept(50, out _);

        var accepted = filter.TryAccept(55, out var reason);

        Assert.True(accepted);
        Assert.Null(reason);
        Assert.Equal(55, filter.LastAccepted);
    }

    [Fact]
    public void TryAccept_RejectsStepBeyondMaximum()
    {
        var filter = new PlausibilityFilter(maxStepPercent: 5);
        filter.TryAccept(50, out _);

        var accepted = filter.TryAccept(55.1, out var reason);

        Assert.False(accepted);
        Assert.NotNull(reason);
        // Zuletzt akzeptierter Wert bleibt unverändert, der Ausreißer darf
        // die Referenz für zukünftige Prüfungen nicht verfälschen.
        Assert.Equal(50, filter.LastAccepted);
    }

    [Fact]
    public void TryAccept_RejectsSuddenDropAsWellAsSuddenRise()
    {
        var filter = new PlausibilityFilter(maxStepPercent: 5);
        filter.TryAccept(50, out _);

        var accepted = filter.TryAccept(40, out var reason);

        Assert.False(accepted);
        Assert.NotNull(reason);
    }

    [Fact]
    public void TryAccept_AllowsSlowContinuousDecline()
    {
        var filter = new PlausibilityFilter(maxStepPercent: 5);

        Assert.True(filter.TryAccept(80, out _));
        Assert.True(filter.TryAccept(76, out _));
        Assert.True(filter.TryAccept(72, out _));
        Assert.True(filter.TryAccept(68, out _));

        Assert.Equal(68, filter.LastAccepted);
    }

    [Fact]
    public void Constructor_ThrowsForNonPositiveMaxStep()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlausibilityFilter(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlausibilityFilter(-1));
    }
}
