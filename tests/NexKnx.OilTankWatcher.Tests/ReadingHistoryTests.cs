using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher.Tests;

public class ReadingHistoryTests
{
    [Fact]
    public void Add_AccumulatesSamples_InInsertionOrder()
    {
        var history = new ReadingHistory(TimeSpan.FromDays(30));
        var now = DateTimeOffset.UtcNow;

        history.Add(new LevelSample(now.AddMinutes(-2), 80));
        history.Add(new LevelSample(now.AddMinutes(-1), 75));
        history.Add(new LevelSample(now, 70));

        history.Samples.Count.ShouldBe(3);
        history.Samples[0].Percent.ShouldBe(80);
        history.Samples[1].Percent.ShouldBe(75);
        history.Samples[2].Percent.ShouldBe(70);
    }

    [Fact]
    public void Add_PrunesSamplesOlderThanRetention_RelativeToTheSampleJustAdded()
    {
        var history = new ReadingHistory(TimeSpan.FromDays(30));
        var now = DateTimeOffset.UtcNow;

        // Far outside the retention window, relative to the second Add call.
        history.Add(new LevelSample(now.AddDays(-40), 90));
        history.Add(new LevelSample(now, 60));

        history.Samples.ShouldHaveSingleItem();
        history.Samples[0].Percent.ShouldBe(60);
    }

    [Fact]
    public void Add_KeepsSamplesWithinRetentionWindow()
    {
        var history = new ReadingHistory(TimeSpan.FromDays(30));
        var now = DateTimeOffset.UtcNow;

        history.Add(new LevelSample(now.AddDays(-10), 90));
        history.Add(new LevelSample(now, 60));

        history.Samples.Count.ShouldBe(2);
    }

    [Fact]
    public void Add_PruningIsRelativeToLatestAddedSample_NotWallClockTime()
    {
        // The implementation prunes relative to the timestamp of the sample
        // just added (not relative to DateTimeOffset.UtcNow) - an old value
        // that is still within the retention window relative to the second
        // sample is kept.
        var history = new ReadingHistory(TimeSpan.FromDays(30));
        var baseline = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        history.Add(new LevelSample(baseline, 90));
        history.Add(new LevelSample(baseline.AddDays(10), 60));

        history.Samples.Count.ShouldBe(2);
    }
}
