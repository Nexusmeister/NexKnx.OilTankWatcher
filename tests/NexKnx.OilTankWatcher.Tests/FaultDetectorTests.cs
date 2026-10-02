using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher.Tests;

public class FaultDetectorTests
{
    [Fact]
    public void IsFaulty_False_ShortlyAfterStart()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var detector = new FaultDetector(TimeSpan.FromHours(12), start);

        var faulty = detector.IsFaulty(start.AddHours(1));

        Assert.False(faulty);
    }

    [Fact]
    public void IsFaulty_True_WhenNoValueReceivedSinceStartBeyondThreshold()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var detector = new FaultDetector(TimeSpan.FromHours(12), start);

        var faulty = detector.IsFaulty(start.AddHours(12).AddMinutes(1));

        Assert.True(faulty);
    }

    [Fact]
    public void IsFaulty_False_AfterMarkValidResetsTheClock()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var detector = new FaultDetector(TimeSpan.FromHours(12), start);

        detector.MarkValid(start.AddHours(11));
        var faulty = detector.IsFaulty(start.AddHours(12).AddMinutes(1));

        Assert.False(faulty);
    }

    [Fact]
    public void IsFaulty_True_AfterThresholdElapsedSinceLastValidValue()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var detector = new FaultDetector(TimeSpan.FromHours(12), start);

        detector.MarkValid(start);
        var faulty = detector.IsFaulty(start.AddHours(12).AddSeconds(1));

        Assert.True(faulty);
    }
}
