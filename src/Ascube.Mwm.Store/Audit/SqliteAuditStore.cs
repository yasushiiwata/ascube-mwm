using Microsoft.Data.Sqlite;

namespace Ascube.Mwm.Store.Audit;

/// <summary>
/// 監査ログ（AuditCFind/AuditCFindItem）の読み書き。ワークリストDBとは別ファイル
/// （<see cref="AuditStoreOptions"/> 参照）。書き込みは SCP、読み取りは主に mwm-admin explain-query が行う。
/// </summary>
public sealed class SqliteAuditStore : IAuditWriter, IAuditReader
{
    private readonly string _connectionString;

    public SqliteAuditStore(AuditStoreOptions options)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = options.DatabasePath }.ToString();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        EnsureSchema(connection);
    }

    public async Task<long> RecordCFindAsync(AuditCFindRecord record, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction();

        long runId;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO AuditCFind
                  (TimestampUtc, ProfileId, CalledAe, CallingAe, RequestJson, CriteriaJson,
                   Clamped, ResultCount, DurationMs, Status, Explain, PeerAborted)
                VALUES
                  ($ts, $profile, $calledAe, $callingAe, $requestJson, $criteriaJson,
                   $clamped, $resultCount, $durationMs, $status, $explain, $peerAborted);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$ts", record.TimestampUtc.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$profile", record.ProfileId);
            command.Parameters.AddWithValue("$calledAe", record.CalledAe);
            command.Parameters.AddWithValue("$callingAe", record.CallingAe);
            command.Parameters.AddWithValue("$requestJson", record.RequestJson);
            command.Parameters.AddWithValue("$criteriaJson", record.CriteriaJson);
            command.Parameters.AddWithValue("$clamped", record.Clamped ? 1 : 0);
            command.Parameters.AddWithValue("$resultCount", record.ResultCount);
            command.Parameters.AddWithValue("$durationMs", record.DurationMs);
            command.Parameters.AddWithValue("$status", record.Status);
            command.Parameters.AddWithValue("$explain", (object?)record.Explain ?? DBNull.Value);
            command.Parameters.AddWithValue("$peerAborted", record.PeerAborted ? 1 : 0);

            runId = (long)(await command.ExecuteScalarAsync(ct))!;
        }

        foreach (var item in record.Items)
        {
            await using var itemCommand = connection.CreateCommand();
            itemCommand.Transaction = transaction;
            itemCommand.CommandText = """
                INSERT INTO AuditCFindItem (RunId, ItemIndex, ProvenanceJson, SuppressedReason)
                VALUES ($runId, $itemIndex, $provenance, $suppressedReason);
                """;
            itemCommand.Parameters.AddWithValue("$runId", runId);
            itemCommand.Parameters.AddWithValue("$itemIndex", item.ItemIndex);
            itemCommand.Parameters.AddWithValue("$provenance", (object?)item.ProvenanceJson ?? DBNull.Value);
            itemCommand.Parameters.AddWithValue("$suppressedReason", (object?)item.SuppressedReason ?? DBNull.Value);
            await itemCommand.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return runId;
    }

    public Task<AuditCFindRecord?> GetCFindAsync(long runId, CancellationToken ct = default) =>
        LoadOneAsync("WHERE RunId = $runId", cmd => cmd.Parameters.AddWithValue("$runId", runId), ct);

    public Task<AuditCFindRecord?> GetLatestCFindAsync(CancellationToken ct = default) =>
        LoadOneAsync("ORDER BY RunId DESC LIMIT 1", _ => { }, ct);

    private async Task<AuditCFindRecord?> LoadOneAsync(string whereOrOrderClause, Action<SqliteCommand> bindParams, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        AuditCFindRecord? record = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT RunId, TimestampUtc, ProfileId, CalledAe, CallingAe, RequestJson, CriteriaJson,
                       Clamped, ResultCount, DurationMs, Status, Explain, PeerAborted
                FROM AuditCFind {whereOrOrderClause};
                """;
            bindParams(command);

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                record = new AuditCFindRecord
                {
                    RunId = reader.GetInt64(0),
                    TimestampUtc = DateTimeOffset.Parse(reader.GetString(1)).ToUniversalTime(),
                    ProfileId = reader.GetString(2),
                    CalledAe = reader.GetString(3),
                    CallingAe = reader.GetString(4),
                    RequestJson = reader.GetString(5),
                    CriteriaJson = reader.GetString(6),
                    Clamped = reader.GetInt64(7) != 0,
                    ResultCount = (int)reader.GetInt64(8),
                    DurationMs = reader.GetInt64(9),
                    Status = reader.GetString(10),
                    Explain = reader.IsDBNull(11) ? null : reader.GetString(11),
                    PeerAborted = reader.GetInt64(12) != 0,
                };
            }
        }

        if (record is null)
        {
            return null;
        }

        var items = new List<AuditCFindItemRecord>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT ItemIndex, ProvenanceJson, SuppressedReason
                FROM AuditCFindItem WHERE RunId = $runId ORDER BY ItemIndex;
                """;
            command.Parameters.AddWithValue("$runId", record.RunId);

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                items.Add(new AuditCFindItemRecord
                {
                    ItemIndex = (int)reader.GetInt64(0),
                    ProvenanceJson = reader.IsDBNull(1) ? null : reader.GetString(1),
                    SuppressedReason = reader.IsDBNull(2) ? null : reader.GetString(2),
                });
            }
        }

        return record with { Items = items };
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA busy_timeout = 5000;

            CREATE TABLE IF NOT EXISTS AuditCFind (
              RunId         INTEGER PRIMARY KEY AUTOINCREMENT,
              TimestampUtc  TEXT NOT NULL,
              ProfileId     TEXT NOT NULL,
              CalledAe      TEXT NOT NULL,
              CallingAe     TEXT NOT NULL,
              RequestJson   TEXT NOT NULL,
              CriteriaJson  TEXT NOT NULL,
              Clamped       INTEGER NOT NULL DEFAULT 0,
              ResultCount   INTEGER NOT NULL,
              DurationMs    INTEGER NOT NULL,
              Status        TEXT NOT NULL,
              Explain       TEXT,
              PeerAborted   INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS AuditCFindItem (
              RunId             INTEGER NOT NULL REFERENCES AuditCFind(RunId),
              ItemIndex         INTEGER NOT NULL,
              ProvenanceJson    TEXT,
              SuppressedReason  TEXT,
              PRIMARY KEY (RunId, ItemIndex)
            );
            """;
        command.ExecuteNonQuery();
    }
}
