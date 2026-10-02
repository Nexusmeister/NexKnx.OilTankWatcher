using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher.Persistence;

public interface IHistoryRepository : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LevelSample>> LoadRecentAsync(TimeSpan retention, CancellationToken cancellationToken);

    Task AppendAsync(LevelSample sample, CancellationToken cancellationToken);

    Task PruneAsync(TimeSpan retention, CancellationToken cancellationToken);
}
