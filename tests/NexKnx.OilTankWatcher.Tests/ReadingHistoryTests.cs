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

        // Weit außerhalb des Retention-Fensters, bezogen auf den zweiten Add-Aufruf.
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
        // Die Implementierung prunt relativ zum Zeitstempel des gerade
        // hinzugefügten Samples (nicht relativ zu DateTimeOffset.UtcNow) -
        // ein alter, aber noch innerhalb der Retention bezogen auf den
        // zweiten Sample liegender Wert bleibt erhalten.
        var history = new ReadingHistory(TimeSpan.FromDays(30));
        var baseline = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        history.Add(new LevelSample(baseline, 90));
        history.Add(new LevelSample(baseline.AddDays(10), 60));

        history.Samples.Count.ShouldBe(2);
    }
}
