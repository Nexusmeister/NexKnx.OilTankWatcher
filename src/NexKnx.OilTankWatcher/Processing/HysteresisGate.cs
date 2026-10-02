namespace NexKnx.OilTankWatcher.Processing;

/// <summary>
/// Binärer Zustand mit Hysterese: aktiviert bei Unterschreiten/Erreichen der
/// unteren Schwelle, deaktiviert erst beim Überschreiten der höheren
/// Rücknahmeschwelle. Verhindert Prellen (Flackern) nahe der Schaltschwelle.
/// </summary>
public sealed class HysteresisGate
{
    private readonly double _activateAtOrBelow;
    private readonly double _releaseAbove;
    private bool _active;

    public HysteresisGate(double activateAtOrBelow, double releaseAbove)
    {
        if (releaseAbove <= activateAtOrBelow)
        {
            throw new ArgumentException(
                "Die Rücknahmeschwelle muss größer als die Aktivierungsschwelle sein (Hysterese-Band).",
                nameof(releaseAbove));
        }

        _activateAtOrBelow = activateAtOrBelow;
        _releaseAbove = releaseAbove;
    }

    public bool IsActive => _active;

    public bool Update(double value)
    {
        if (!_active && value <= _activateAtOrBelow)
        {
            _active = true;
        }
        else if (_active && value > _releaseAbove)
        {
            _active = false;
        }

        return _active;
    }
}
