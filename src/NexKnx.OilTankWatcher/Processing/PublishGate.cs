namespace NexKnx.OilTankWatcher.Processing;

/// <summary>
/// Decides whether a new state should be written to the KNX bus:
/// immediately on a relevant change, otherwise only cyclically every X
/// minutes. Prevents unnecessary bus traffic on every single reading.
/// </summary>
public sealed class PublishGate
{
    private readonly TimeSpan _interval;
    private readonly double _changeThresholdPercent;

    private DateTimeOffset? _lastSentAt;
    private double? _lastSentPercent;
    private bool? _lastSentWarning;
    private bool? _lastSentFault;

    public PublishGate(TimeSpan interval, double changeThresholdPercent)
    {
        _interval = interval;
        _changeThresholdPercent = changeThresholdPercent;
    }

    public bool ShouldPublish(DateTimeOffset now, double percent, bool warning, bool fault)
    {
        if (_lastSentAt is null)
        {
            return true;
        }

        if (warning != _lastSentWarning || fault != _lastSentFault)
        {
            return true;
        }

        if (_lastSentPercent is { } lastPercent && Math.Abs(percent - lastPercent) >= _changeThresholdPercent)
        {
            return true;
        }

        return now - _lastSentAt.Value >= _interval;
    }

    public void MarkSent(DateTimeOffset now, double percent, bool warning, bool fault)
    {
        _lastSentAt = now;
        _lastSentPercent = percent;
        _lastSentWarning = warning;
        _lastSentFault = fault;
    }
}
