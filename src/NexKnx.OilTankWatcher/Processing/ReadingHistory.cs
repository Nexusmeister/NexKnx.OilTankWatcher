namespace NexKnx.OilTankWatcher.Processing;

/// <summary>
/// Hält die Füllstands-Historie für ein gleitendes Zeitfenster (z. B. 30 Tage)
/// als Grundlage für die Restreichweitenschätzung. Die Historie liegt nur im
/// Arbeitsspeicher und beginnt nach einem Neustart des Dienstes neu.
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
