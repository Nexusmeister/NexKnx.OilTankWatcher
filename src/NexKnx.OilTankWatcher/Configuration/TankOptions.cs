namespace NexKnx.OilTankWatcher.Configuration;

public sealed class TankOptions
{
    public const string SectionName = "Tank";

    /// Nennvolumen des Tanks in Litern (100 % Füllstand).
    public double NominalVolumeLiters { get; set; } = 9100;

    /// Maximal plausible Änderung zwischen zwei Messungen in Prozentpunkten.
    public double MaxStepPercent { get; set; } = 5;

    /// Fenstergröße für die Median-Glättung.
    public int MedianWindowSize { get; set; } = 5;
}
