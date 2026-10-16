namespace NexKnx.OilTankWatcher.Configuration;

public sealed class TankOptions
{
    public const string SectionName = "Tank";

    /// Nominal volume of the tank in liters (100 % fill level).
    public double NominalVolumeLiters { get; set; } = 9100;

    /// Maximum plausible change between two readings in percentage points.
    public double MaxStepPercent { get; set; } = 5;

    /// Window size for the median smoothing.
    public int MedianWindowSize { get; set; } = 5;
}
