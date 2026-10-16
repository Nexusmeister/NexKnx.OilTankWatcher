namespace NexKnx.OilTankWatcher.Configuration;

public sealed class RuntimeEstimationOptions
{
    public const string SectionName = "RuntimeEstimation";

    public bool Enabled { get; set; } = true;

    /// History window for the linear regression.
    public int HistoryDays { get; set; } = 30;

    /// Minimum number of data points before an estimate is produced.
    public int MinimumSamples { get; set; } = 5;

    /// Path to the SQLite database file for the persistent history.
    /// For Docker deployments, point this at a mounted volume (see
    /// docker-compose.yml) so the history survives restarts.
    public string DatabasePath { get; set; } = "history.db";
}
