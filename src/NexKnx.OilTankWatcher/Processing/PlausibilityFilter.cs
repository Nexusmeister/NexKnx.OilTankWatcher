namespace NexKnx.OilTankWatcher.Processing;

/// <summary>
/// Verwirft Messwerte außerhalb des gültigen Bereichs (0-100 %) sowie
/// Ausreißer, die sprunghaft (mehr als die konfigurierte maximale Änderung
/// pro Messung) vom zuletzt akzeptierten Wert abweichen.
/// </summary>
public sealed class PlausibilityFilter
{
    private readonly double _maxStepPercent;
    private double? _lastAccepted;

    public PlausibilityFilter(double maxStepPercent)
    {
        if (maxStepPercent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStepPercent), "Die maximale Änderung muss größer als 0 sein.");
        }

        _maxStepPercent = maxStepPercent;
    }

    public double? LastAccepted => _lastAccepted;

    public bool TryAccept(double rawPercent, out string? rejectReason)
    {
        if (rawPercent < 0 || rawPercent > 100)
        {
            rejectReason = $"Wert {rawPercent:0.##} liegt außerhalb des gültigen Bereichs 0-100%.";
            return false;
        }

        if (_lastAccepted is { } previous && Math.Abs(rawPercent - previous) > _maxStepPercent)
        {
            rejectReason =
                $"Sprung von {previous:0.##}% auf {rawPercent:0.##}% überschreitet die maximale " +
                $"Änderung von {_maxStepPercent:0.##} Prozentpunkten pro Messung.";
            return false;
        }

        _lastAccepted = rawPercent;
        rejectReason = null;
        return true;
    }
}
