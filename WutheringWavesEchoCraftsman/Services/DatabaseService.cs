using System.IO;
using Microsoft.Data.Sqlite;

namespace WutheringWavesEchoCraftsman.Services;

public sealed class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS echo_results (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at TEXT NOT NULL,
                raw_ocr_text TEXT NOT NULL,
                decision TEXT NOT NULL,
                valid_count INTEGER NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertResultAsync(string rawOcrText, string decision, int validCount, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO echo_results (created_at, raw_ocr_text, decision, valid_count)
            VALUES ($createdAt, $rawOcrText, $decision, $validCount);
            """;
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.Now.ToString("O"));
        command.Parameters.AddWithValue("$rawOcrText", rawOcrText);
        command.Parameters.AddWithValue("$decision", decision);
        command.Parameters.AddWithValue("$validCount", validCount);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EchoResultRecord>> GetRecentResultsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        var records = new List<EchoResultRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, created_at, raw_ocr_text, decision, valid_count
            FROM echo_results
            ORDER BY id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new EchoResultRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4)));
        }

        return records;
    }
}

public sealed record EchoResultRecord(long Id, string CreatedAt, string RawOcrText, string Decision, int ValidCount);
