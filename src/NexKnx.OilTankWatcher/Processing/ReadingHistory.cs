namespace NexKnx.OilTankWatcher.Processing;

/// <summary>
/// Holds the fill-level history for a sliding time window (e.g. 30 days) as
/// the basis for the remaining runtime estimation. The history only lives
/// in memory and starts fresh after a restart of the service.
/// </summary>
public sealed class ReadingHistory
{
    private readonly TimeSpan _retention;
    private readonly List<LevelSample> _samples = new();

    public ReadingHistory(TimeSpan retention)
    {
        _retention = retention;
    }

    public void Add(LevelSample sample)
    {
        _samples.Add(sample);
        var cutoff = sample.Timestamp - _retention;
        _samples.RemoveAll(s => s.Timestamp < cutoff);
    }

    public IReadOnlyList<LevelSample> Samples => _samples;
}
