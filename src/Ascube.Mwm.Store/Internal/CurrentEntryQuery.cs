using Ascube.Mwm.Abstractions;
using Microsoft.Data.Sqlite;

namespace Ascube.Mwm.Store.Internal;

/// <summary>
/// CurrentEntry の読み出し。<see cref="SqliteWorklistWriter.GetCurrentAsync"/> と
/// <see cref="SqliteWorklistRepository"/> の両方から使う共通クエリ。
/// </summary>
internal static class CurrentEntryQuery
{
    public static async Task<CurrentEntryRow?> LoadAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = """
            SELECT
              ce.WorkItemId, ce.SetAtUtc, ce.ExpiresAtUtc,
              ua.StudyInstanceUid,
              wi.StablePatientId, wi.ScheduledDate, wi.AccessionNumber,
              wi.RequestedProcedureId, wi.RequestedProcedureDesc,
              wi.PatientSizeM, wi.PatientWeightKg, wi.SourceMessageId,
              p.FamilyNameKanji, p.GivenNameKanji, p.FamilyNameKana, p.GivenNameKana, p.BirthDate, p.Sex
            FROM CurrentEntry ce
            JOIN WorkItem wi ON wi.WorkItemId = ce.WorkItemId
            JOIN Patient p ON p.StablePatientId = wi.StablePatientId
            LEFT JOIN UidAllocation ua ON ua.WorkItemId = ce.WorkItemId
            WHERE ce.Singleton = 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new CurrentEntryRow(
            WorkItemId: reader.GetString(0),
            SetAtUtc: DateTimeOffset.Parse(reader.GetString(1)),
            ExpiresAtUtc: DateTimeOffset.Parse(reader.GetString(2)),
            StudyInstanceUid: reader.IsDBNull(3) ? null : reader.GetString(3),
            StablePatientId: reader.GetString(4),
            ScheduledDate: reader.GetString(5),
            AccessionNumber: reader.IsDBNull(6) ? null : reader.GetString(6),
            RequestedProcedureId: reader.IsDBNull(7) ? null : reader.GetString(7),
            RequestedProcedureDesc: reader.IsDBNull(8) ? null : reader.GetString(8),
            PatientSizeM: reader.IsDBNull(9) ? null : reader.GetDouble(9),
            PatientWeightKg: reader.IsDBNull(10) ? null : reader.GetDouble(10),
            SourceMessageId: reader.IsDBNull(11) ? null : reader.GetString(11),
            FamilyNameKanji: reader.IsDBNull(12) ? null : reader.GetString(12),
            GivenNameKanji: reader.IsDBNull(13) ? null : reader.GetString(13),
            FamilyNameKana: reader.IsDBNull(14) ? null : reader.GetString(14),
            GivenNameKana: reader.IsDBNull(15) ? null : reader.GetString(15),
            BirthDate: reader.IsDBNull(16) ? null : reader.GetString(16),
            Sex: (Sex)reader.GetInt32(17));
    }
}
