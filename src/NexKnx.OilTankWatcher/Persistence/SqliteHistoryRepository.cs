using System.Globalization;
using Microsoft.Data.Sqlite;
using NexKnx.OilTankWatcher.Processing;

namespace NexKnx.OilTankWatcher.Persistence;

/// <summary>
/// Persistiert die Füllstandshistorie (für die Restreichweitenschätzung)
/// in einer lokalen SQLite-Datenbank, damit sie einen Neustart des Dienstes
/// überlebt. Eine einzige, langlebige Verbindung wird für die Lebensdauer
/// des Repositorys offen gehalten (Standardmuster für SQLite in einem
/// Single-Writer-Prozess).
///
/// API-Stand gegen die tatsächlich installierte Paketversion
/// (Microsoft.Data.Sqlite 10.0.12) funktional verifiziert, nicht aus dem
/// Gedächtnis übernommen: Verbindungsaufbau, Retention-Filterung via
/// WHERE-Parameter, "INSERT OR REPLACE" (Ersetzen statt Duplizieren bei
/// gleichem Zeitstempel) und Prune-Löschung wurden gegen eine echte
/// In-Memory-SQLite-Datenbank durchgespielt.
/// </summary>
public sealed class SqliteHistoryRepository : IHistoryRepository
{
    private readonly SqliteConnection _connection;

    public SqliteHistoryRepository(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection($"Data Source={databasePath}");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _connection.OpenAsync(cancellationToken);

        var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS LevelHistory (
                TimestampUtc TEXT NOT NULL PRIMARY KEY,
                Percent REAL NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LevelSample>> LoadRecentAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow - retention;

        var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT TimestampUtc, Percent FROM LevelHistory
            WHERE TimestampUtc >= $cutoff
            ORDER BY TimestampUtc ASC;
            """;
        command.Parameters.AddWithValue("$cutoff", FormatTimestamp(cutoff));

        var results = new List<LevelSample>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var timestamp = DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
            var percent = reader.GetDouble(1);
            results.Add(new LevelSample(timestamp, percent));
        }

        return results;
    }

    public async Task AppendAsync(LevelSample sample, CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO LevelHistory (TimestampUtc, Percent)
            VALUES ($timestamp, $percent);
            """;
        command.Parameters.AddWithValue("$timestamp", FormatTimestamp(sample.Timestamp));
        command.Parameters.AddWithValue("$percent", sample.Percent);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PruneAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow - retention;

        var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM LevelHistory WHERE TimestampUtc < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", FormatTimestamp(cutoff));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToString("O", CultureInfo.InvariantCulture);

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
