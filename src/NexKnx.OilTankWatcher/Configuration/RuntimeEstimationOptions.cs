namespace NexKnx.OilTankWatcher.Configuration;

public sealed class RuntimeEstimationOptions
{
    public const string SectionName = "RuntimeEstimation";

    public bool Enabled { get; set; } = true;

    /// Historienfenster für die lineare Regression.
    public int HistoryDays { get; set; } = 30;

    /// Mindestanzahl an Messpunkten, bevor eine Schätzung ausgegeben wird.
    public int MinimumSamples { get; set; } = 5;

    /// Pfad zur SQLite-Datenbankdatei für die persistente Historie.
    /// Für Docker-Deployments auf ein gemountetes Volume zeigen lassen
    /// (siehe docker-compose.yml), damit die Historie Neustarts überlebt.
    public string DatabasePath { get; set; } = "history.db";
}
