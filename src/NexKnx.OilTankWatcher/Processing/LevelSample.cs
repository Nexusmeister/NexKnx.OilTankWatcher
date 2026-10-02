namespace NexKnx.OilTankWatcher.Processing;

public readonly record struct LevelSample(DateTimeOffset Timestamp, double Percent);
