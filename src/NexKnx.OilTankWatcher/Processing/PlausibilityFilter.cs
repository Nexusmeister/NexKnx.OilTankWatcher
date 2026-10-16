namespace NexKnx.OilTankWatcher.Processing;

/// <summary>
/// Rejects readings outside the valid range (0-100 %) as well as outliers
/// that deviate abruptly (more than the configured maximum change per
/// reading) from the last accepted value.
/// </summary>
public sealed class PlausibilityFilter
{
    private readonly double _maxStepPercent;
    private double? _lastAccepted;

    public PlausibilityFilter(double maxStepPercent)
    {
        if (maxStepPercent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStepPercent), "The maximum change must be greater than 0.");
        }

        _maxStepPercent = maxStepPercent;
    }

    public double? LastAccepted => _lastAccepted;

    public bool TryAccept(double rawPercent, out string? rejectReason)
    {
        if (rawPercent < 0 || rawPercent > 100)
        {
            rejectReason = $"Value {rawPercent:0.##} is outside the valid range 0-100%.";
            return false;
        }

        if (_lastAccepted is { } previous && Math.Abs(rawPercent - previous) > _maxStepPercent)
        {
            rejectReason =
                $"Jump from {previous:0.##}% to {rawPercent:0.##}% exceeds the maximum " +
                $"change of {_maxStepPercent:0.##} percentage points per reading.";
            return false;
        }

        _lastAccepted = rawPercent;
        rejectReason = null;
        return true;
    }
}
