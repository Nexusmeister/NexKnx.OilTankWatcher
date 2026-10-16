namespace NexKnx.OilTankWatcher.Processing;

/// <summary>
/// Detects the fault condition "no valid reading for more than X hours".
/// The reference point in time is set to the service's start time at
/// startup, so a complete failure of the data source since startup is also
/// detected.
/// </summary>
public sealed class FaultDetector
{
    private readonly TimeSpan _maxAge;
    private DateTimeOffset _lastValidAt;

    public FaultDetector(TimeSpan maxAge, DateTimeOffset startedAt)
    {
        _maxAge = maxAge;
        _lastValidAt = startedAt;
    }

    public void MarkValid(DateTimeOffset now) => _lastValidAt = now;

    public bool IsFaulty(DateTimeOffset now) => now - _lastValidAt > _maxAge;
}
