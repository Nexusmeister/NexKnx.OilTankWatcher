namespace NexKnx.OilTankWatcher.Processing;

/// <summary>
/// Schätzt die verbleibende Reichweite in Tagen per linearer Regression
/// (Methode der kleinsten Quadrate) über die Füllstandshistorie. Liefert
/// null, wenn zu wenige Messpunkte vorliegen oder der Füllstand nicht fällt
/// (z. B. kurz nach einer Befüllung).
/// </summary>
public static class RuntimeEstimator
{
    public static double? EstimateDaysRemaining(IReadOnlyList<LevelSample> samples, int minimumSamples)
    {
        if (samples.Count < minimumSamples)
        {
            return null;
        }

        var baseline = samples[0].Timestamp;
        var xs = samples.Select(s => (s.Timestamp - baseline).TotalDays).ToArray();
        var ys = samples.Select(s => s.Percent).ToArray();

        var n = xs.Length;
        var sumX = xs.Sum();
        var sumY = ys.Sum();
        var sumXY = xs.Zip(ys, (x, y) => x * y).Sum();
        var sumXX = xs.Sum(x => x * x);

        var denominator = n * sumXX - sumX * sumX;
        if (Math.Abs(denominator) < 1e-9)
        {
            return null;
        }

        // Steigung der Ausgleichsgeraden in Prozentpunkten pro Tag.
        var slopePerDay = (n * sumXY - sumX * sumY) / denominator;
        if (slopePerDay >= -1e-6)
        {
            // Füllstand steigt oder stagniert (z. B. nach einer Befüllung) -
            // eine Restreichweite ist in diesem Fall nicht sinnvoll definiert.
            return null;
        }

        var currentPercent = ys[^1];
        var daysRemaining = currentPercent / -slopePerDay;
        return Math.Max(0, daysRemaining);
    }
}
