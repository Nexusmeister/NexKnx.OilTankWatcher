namespace NexKnx.OilTankWatcher.Processing;

/// <summary>
/// Glättet eine Messreihe durch den Median der letzten N gültigen Werte
/// (gleitendes Fenster).
/// </summary>
public sealed class MedianSmoother
{
    private readonly int _windowSize;
    private readonly Queue<double> _window = new();

    public MedianSmoother(int windowSize)
    {
        if (windowSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSize), "Die Fenstergröße muss größer als 0 sein.");
        }

        _windowSize = windowSize;
    }

    public int Count => _window.Count;

    public double Add(double value)
    {
        _window.Enqueue(value);
        while (_window.Count > _windowSize)
        {
            _window.Dequeue();
        }

        var sorted = _window.Order().ToArray();
        var mid = sorted.Length / 2;

        return sorted.Length % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }
}
