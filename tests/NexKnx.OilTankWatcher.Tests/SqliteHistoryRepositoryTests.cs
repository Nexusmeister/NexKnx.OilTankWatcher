using NexKnx.OilTankWatcher.Persistence;
using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher.Tests;

public class SqliteHistoryRepositoryTests
{
    private static async Task<SqliteHistoryRepository> CreateInitializedAsync()
    {
        var repository = new SqliteHistoryRepository(":memory:");
        await repository.InitializeAsync(CancellationToken.None);
        return repository;
    }

    [Fact]
    public async Task LoadRecentAsync_ReturnsEmpty_WhenNothingStoredYet()
    {
        await using var repository = await CreateInitializedAsync();

        var result = await repository.LoadRecentAsync(TimeSpan.FromDays(30), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task AppendAsync_ThenLoadRecentAsync_ReturnsTheSample()
    {
        await using var repository = await CreateInitializedAsync();
        var sample = new LevelSample(DateTimeOffset.UtcNow, 55.5);

        await repository.AppendAsync(sample, CancellationToken.None);
        var result = await repository.LoadRecentAsync(TimeSpan.FromDays(30), CancellationToken.None);

        result.ShouldHaveSingleItem();
        result[0].Percent.ShouldBe(sample.Percent);
        result[0].Timestamp.ShouldBe(sample.Timestamp);
    }

    [Fact]
    public async Task AppendAsync_SameTimestampTwice_ReplacesInsteadOfDuplicating()
    {
        await using var repository = await CreateInitializedAsync();
        var timestamp = DateTimeOffset.UtcNow;

        await repository.AppendAsync(new LevelSample(timestamp, 60), CancellationToken.None);
        await repository.AppendAsync(new LevelSample(timestamp, 61), CancellationToken.None);

        var result = await repository.LoadRecentAsync(TimeSpan.FromDays(30), CancellationToken.None);

        result.ShouldHaveSingleItem();
        result[0].Percent.ShouldBe(61);
    }

    [Fact]
    public async Task LoadRecentAsync_ExcludesSamplesOlderThanRetention()
    {
        await using var repository = await CreateInitializedAsync();
        var now = DateTimeOffset.UtcNow;

        await repository.AppendAsync(new LevelSample(now.AddDays(-40), 90), CancellationToken.None);
        await repository.AppendAsync(new LevelSample(now.AddDays(-10), 70), CancellationToken.None);
        await repository.AppendAsync(new LevelSample(now, 60), CancellationToken.None);

        var result = await repository.LoadRecentAsync(TimeSpan.FromDays(30), CancellationToken.None);

        result.Count.ShouldBe(2);
        result.ShouldNotContain(s => s.Percent == 90);
    }

    [Fact]
    public async Task LoadRecentAsync_ReturnsSamplesOrderedByTimestampAscending()
    {
        await using var repository = await CreateInitializedAsync();
        var now = DateTimeOffset.UtcNow;

        await repository.AppendAsync(new LevelSample(now, 60), CancellationToken.None);
        await repository.AppendAsync(new LevelSample(now.AddDays(-5), 70), CancellationToken.None);
        await repository.AppendAsync(new LevelSample(now.AddDays(-10), 80), CancellationToken.None);

        var result = await repository.LoadRecentAsync(TimeSpan.FromDays(30), CancellationToken.None);

        result[0].Percent.ShouldBe(80);
        result[1].Percent.ShouldBe(70);
        result[2].Percent.ShouldBe(60);
    }

    [Fact]
    public async Task PruneAsync_RemovesSamplesOlderThanRetention()
    {
        await using var repository = await CreateInitializedAsync();
        var now = DateTimeOffset.UtcNow;

        await repository.AppendAsync(new LevelSample(now.AddDays(-40), 90), CancellationToken.None);
        await repository.AppendAsync(new LevelSample(now, 60), CancellationToken.None);

        await repository.PruneAsync(TimeSpan.FromDays(30), CancellationToken.None);

        // Großes Retention-Fenster, damit wir wirklich alles sehen, was nach dem Prune noch da ist.
        var result = await repository.LoadRecentAsync(TimeSpan.FromDays(3650), CancellationToken.None);

        result.ShouldHaveSingleItem();
        result[0].Percent.ShouldBe(60);
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent_AndPreservesExistingData()
    {
        await using var repository = await CreateInitializedAsync();
        await repository.AppendAsync(new LevelSample(DateTimeOffset.UtcNow, 42), CancellationToken.None);

        // CREATE TABLE IF NOT EXISTS darf beim zweiten Aufruf nichts löschen.
        await repository.InitializeAsync(CancellationToken.None);

        var result = await repository.LoadRecentAsync(TimeSpan.FromDays(30), CancellationToken.None);
        result.ShouldHaveSingleItem();
    }
}
