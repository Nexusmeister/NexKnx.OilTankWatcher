namespace NexKnx.OilTankWatcher.Processing;

/// <summary>
/// Estimates the remaining runtime in days via linear regression (least
/// squares method) over the fill-level history. Returns null if there are
/// too few data points or the fill level isn't dropping (e.g. shortly
/// after a refill).
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

        // Slope of the regression line in percentage points per day.
        var slopePerDay = (n * sumXY - sumX * sumY) / denominator;
        if (slopePerDay >= -1e-6)
        {
            // Fill level is rising or stagnant (e.g. after a refill) - a
            // remaining runtime isn't meaningfully defined in this case.
            return null;
        }

        var currentPercent = ys[^1];
        var daysRemaining = currentPercent / -slopePerDay;
        return Math.Max(0, daysRemaining);
    }
}
