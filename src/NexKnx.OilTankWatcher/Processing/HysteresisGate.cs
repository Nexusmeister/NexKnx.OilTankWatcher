namespace NexKnx.OilTankWatcher.Processing;

/// <summary>
/// Binary state with hysteresis: activated when the lower threshold is
/// reached or undercut, only deactivated when the higher recovery
/// threshold is exceeded. Prevents bouncing (flickering) near the
/// switching threshold.
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
                "The recovery threshold must be greater than the activation threshold (hysteresis band).",
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
