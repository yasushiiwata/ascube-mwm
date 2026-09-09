using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Store.Internal;
using Microsoft.Data.Sqlite;

namespace Ascube.Mwm.Store;

/// <summary>
/// BRIDGE-Navi 本体が使う書き込み側。1トランザクションで
/// Patient upsert → WorkItem upsert → UidAllocation 採番 → CurrentEntry 差し替えを行う（実装指示書 v2 §5-T2）。
/// </summary>
public sealed class SqliteWorklistWriter : IWorklistWriter
{
    private readonly MwmStoreOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly string _connectionString;

    public SqliteWorklistWriter(MwmStoreOptions options, TimeProvider? timeProvider = null)
    {
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = options.DatabasePath }.ToString();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        SqliteSchema.EnsureCreated(connection);
    }

    public async Task SetCurrentAsync(WorklistEntry entry, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await SetBusyTimeoutAsync(connection, ct);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        var now = _timeProvider.GetUtcNow();
        var nowText = FormatInstant(now);

        await UpsertPatientAsync(connection, transaction, entry, nowText, ct);
        var workItemId = await UpsertWorkItemAsync(connection, transaction, entry, nowText, ct);
        await EnsureUidAllocationAsync(connection, transaction, workItemId, nowText, ct);
        await UpsertCurrentEntryAsync(connection, transaction, workItemId, now, ct);

        await transaction.CommitAsync(ct);
    }

    public async Task ClearCurrentAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await SetBusyTimeoutAsync(connection, ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CurrentEntry;";
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<WorklistEntry?> GetCurrentAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await SetBusyTimeoutAsync(connection, ct);

        var row = await CurrentEntryQuery.LoadAsync(connection, transaction: null, ct);
        if (row is null || row.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            return null;
        }

        return new WorklistEntry
        {
            StablePatientId = row.StablePatientId,
            FamilyNameKanji = row.FamilyNameKanji,
            GivenNameKanji = row.GivenNameKanji,
            FamilyNameKana = row.FamilyNameKana,
            GivenNameKana = row.GivenNameKana,
            BirthDate = row.BirthDate,
            Sex = row.Sex,
            ScheduledDate = row.ScheduledDate,
            AccessionNumber = row.AccessionNumber,
            RequestedProcedureId = row.RequestedProcedureId,
            RequestedProcedureDesc = row.RequestedProcedureDesc,
            PatientSizeM = row.PatientSizeM,
            PatientWeightKg = row.PatientWeightKg,
            SourceMessageId = row.SourceMessageId,
        };
    }

    private static async Task SetBusyTimeoutAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string FormatInstant(DateTimeOffset instant) => instant.ToString("O");

    private async Task UpsertPatientAsync(SqliteConnection connection, SqliteTransaction transaction, WorklistEntry entry, string nowText, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Patient (StablePatientId, FamilyNameKanji, GivenNameKanji, FamilyNameKana, GivenNameKana, BirthDate, Sex, UpdatedAtUtc)
            VALUES ($id, $familyKanji, $givenKanji, $familyKana, $givenKana, $birth, $sex, $now)
            ON CONFLICT(StablePatientId) DO UPDATE SET
              FamilyNameKanji = excluded.FamilyNameKanji,
              GivenNameKanji  = excluded.GivenNameKanji,
              FamilyNameKana  = excluded.FamilyNameKana,
              GivenNameKana   = excluded.GivenNameKana,
              BirthDate       = excluded.BirthDate,
              Sex             = excluded.Sex,
              UpdatedAtUtc    = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$id", entry.StablePatientId);
        command.Parameters.AddWithValue("$familyKanji", (object?)entry.FamilyNameKanji ?? DBNull.Value);
        command.Parameters.AddWithValue("$givenKanji", (object?)entry.GivenNameKanji ?? DBNull.Value);
        command.Parameters.AddWithValue("$familyKana", (object?)entry.FamilyNameKana ?? DBNull.Value);
        command.Parameters.AddWithValue("$givenKana", (object?)entry.GivenNameKana ?? DBNull.Value);
        command.Parameters.AddWithValue("$birth", (object?)entry.BirthDate ?? DBNull.Value);
        command.Parameters.AddWithValue("$sex", (int)entry.Sex);
        command.Parameters.AddWithValue("$now", nowText);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<string> UpsertWorkItemAsync(SqliteConnection connection, SqliteTransaction transaction, WorklistEntry entry, string nowText, CancellationToken ct)
    {
        var deviceProfileId = _options.DeviceProfileId;

        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT WorkItemId FROM WorkItem
                WHERE StablePatientId = $pid AND ScheduledDate = $date AND DeviceProfileId = $profile;
                """;
            select.Parameters.AddWithValue("$pid", entry.StablePatientId);
            select.Parameters.AddWithValue("$date", entry.ScheduledDate);
            select.Parameters.AddWithValue("$profile", deviceProfileId);

            if (await select.ExecuteScalarAsync(ct) is string existingId)
            {
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE WorkItem SET
                      AccessionNumber = $accession,
                      RequestedProcedureId = $procId,
                      RequestedProcedureDesc = $procDesc,
                      PatientSizeM = $sizeM,
                      PatientWeightKg = $weightKg,
                      SourceMessageId = $sourceId,
                      UpdatedAtUtc = $now
                    WHERE WorkItemId = $id;
                    """;
                AddWorkItemMutableParameters(update, entry, nowText);
                update.Parameters.AddWithValue("$id", existingId);
                await update.ExecuteNonQueryAsync(ct);
                return existingId;
            }
        }

        var newId = Guid.NewGuid().ToString("N");
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO WorkItem
              (WorkItemId, StablePatientId, ScheduledDate, DeviceProfileId,
               AccessionNumber, RequestedProcedureId, RequestedProcedureDesc,
               PatientSizeM, PatientWeightKg, SourceMessageId, CreatedAtUtc, UpdatedAtUtc)
            VALUES
              ($id, $pid, $date, $profile,
               $accession, $procId, $procDesc,
               $sizeM, $weightKg, $sourceId, $now, $now);
            """;
        insert.Parameters.AddWithValue("$id", newId);
        insert.Parameters.AddWithValue("$pid", entry.StablePatientId);
        insert.Parameters.AddWithValue("$date", entry.ScheduledDate);
        insert.Parameters.AddWithValue("$profile", deviceProfileId);
        AddWorkItemMutableParameters(insert, entry, nowText);
        await insert.ExecuteNonQueryAsync(ct);
        return newId;
    }

    private static void AddWorkItemMutableParameters(SqliteCommand command, WorklistEntry entry, string nowText)
    {
        command.Parameters.AddWithValue("$accession", (object?)entry.AccessionNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$procId", (object?)entry.RequestedProcedureId ?? DBNull.Value);
        command.Parameters.AddWithValue("$procDesc", (object?)entry.RequestedProcedureDesc ?? DBNull.Value);
        command.Parameters.AddWithValue("$sizeM", (object?)entry.PatientSizeM ?? DBNull.Value);
        command.Parameters.AddWithValue("$weightKg", (object?)entry.PatientWeightKg ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceId", (object?)entry.SourceMessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", nowText);
    }

    private static async Task EnsureUidAllocationAsync(SqliteConnection connection, SqliteTransaction transaction, string workItemId, string nowText, CancellationToken ct)
    {
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT 1 FROM UidAllocation WHERE WorkItemId = $id;";
            select.Parameters.AddWithValue("$id", workItemId);
            if (await select.ExecuteScalarAsync(ct) is not null)
            {
                return;
            }
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO UidAllocation (WorkItemId, StudyInstanceUid, AllocatedAtUtc)
            VALUES ($id, $uid, $now);
            """;
        insert.Parameters.AddWithValue("$id", workItemId);
        insert.Parameters.AddWithValue("$uid", StudyInstanceUidGenerator.NewUid());
        insert.Parameters.AddWithValue("$now", nowText);
        await insert.ExecuteNonQueryAsync(ct);
    }

    private async Task UpsertCurrentEntryAsync(SqliteConnection connection, SqliteTransaction transaction, string workItemId, DateTimeOffset now, CancellationToken ct)
    {
        var expiresAt = now + _options.CurrentTtl;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CurrentEntry (Singleton, WorkItemId, DeviceProfileId, SetAtUtc, ExpiresAtUtc)
            VALUES (1, $id, $profile, $setAt, $expiresAt)
            ON CONFLICT(Singleton) DO UPDATE SET
              WorkItemId = excluded.WorkItemId,
              DeviceProfileId = excluded.DeviceProfileId,
              SetAtUtc = excluded.SetAtUtc,
              ExpiresAtUtc = excluded.ExpiresAtUtc;
            """;
        command.Parameters.AddWithValue("$id", workItemId);
        command.Parameters.AddWithValue("$profile", _options.DeviceProfileId);
        command.Parameters.AddWithValue("$setAt", FormatInstant(now));
        command.Parameters.AddWithValue("$expiresAt", FormatInstant(expiresAt));
        await command.ExecuteNonQueryAsync(ct);
    }
}
