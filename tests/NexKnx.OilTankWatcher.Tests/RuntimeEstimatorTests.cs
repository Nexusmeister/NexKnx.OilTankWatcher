using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher.Tests;

public class RuntimeEstimatorTests
{
    [Fact]
    public void EstimateDaysRemaining_Null_WhenTooFewSamples()
    {
        var samples = new[]
        {
            new LevelSample(DateTimeOffset.UtcNow, 50),
            new LevelSample(DateTimeOffset.UtcNow.AddDays(1), 49)
        };

        var result = RuntimeEstimator.EstimateDaysRemaining(samples, minimumSamples: 5);

        Assert.Null(result);
    }

    [Fact]
    public void EstimateDaysRemaining_ComputesLinearDecline()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var samples = new[]
        {
            new LevelSample(start, 80),
            new LevelSample(start.AddDays(5), 75),
            new LevelSample(start.AddDays(10), 70),
            new LevelSample(start.AddDays(15), 65),
            new LevelSample(start.AddDays(20), 60),
        };

        // Steigung: -1 Prozentpunkt/Tag, aktueller Wert 60% -> 60 Tage Restreichweite.
        var result = RuntimeEstimator.EstimateDaysRemaining(samples, minimumSamples: 5);

        Assert.NotNull(result);
        Assert.Equal(60, result!.Value, precision: 3);
    }

    [Fact]
    public void EstimateDaysRemaining_Null_WhenLevelIsRising()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var samples = new[]
        {
            new LevelSample(start, 30),
            new LevelSample(start.AddDays(1), 50),
            new LevelSample(start.AddDays(2), 70),
            new LevelSample(start.AddDays(3), 85),
            new LevelSample(start.AddDays(4), 95),
        };

        var result = RuntimeEstimator.EstimateDaysRemaining(samples, minimumSamples: 5);

        Assert.Null(result);
    }

    [Fact]
    public void EstimateDaysRemaining_Null_WhenLevelIsFlat()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var samples = Enumerable.Range(0, 5)
            .Select(i => new LevelSample(start.AddDays(i), 50))
            .ToArray();

        var result = RuntimeEstimator.EstimateDaysRemaining(samples, minimumSamples: 5);

        Assert.Null(result);
    }

    [Fact]
    public void EstimateDaysRemaining_NeverNegative()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var samples = new[]
        {
            new LevelSample(start, 10),
            new LevelSample(start.AddDays(1), 8),
            new LevelSample(start.AddDays(2), 6),
            new LevelSample(start.AddDays(3), 4),
            new LevelSample(start.AddDays(4), 2),
        };

        var result = RuntimeEstimator.EstimateDaysRemaining(samples, minimumSamples: 5);

        Assert.NotNull(result);
        Assert.True(result!.Value >= 0);
    }
}
