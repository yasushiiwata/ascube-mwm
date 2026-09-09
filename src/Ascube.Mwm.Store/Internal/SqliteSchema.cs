using Microsoft.Data.Sqlite;

namespace Ascube.Mwm.Store.Internal;

/// <summary>
/// 実装指示書 v2 §5-T2 のDDL。1人モデルにより ActiveContext を CurrentEntry に置換。
/// Patient / WorkItem / UidAllocation は監査・UID再利用のため履歴として残す。
/// </summary>
internal static class SqliteSchema
{
    public static void EnsureCreated(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA busy_timeout = 5000;

            CREATE TABLE IF NOT EXISTS Patient (
              StablePatientId TEXT PRIMARY KEY,
              FamilyNameKanji TEXT,
              GivenNameKanji  TEXT,
              FamilyNameKana  TEXT,
              GivenNameKana   TEXT,
              BirthDate       TEXT,
              Sex             INTEGER NOT NULL,
              UpdatedAtUtc    TEXT NOT NULL
            );

            -- AccessionNumber を主キーにしない（禁止事項7：年度で重複しうる／未採番の検査を保持できない）
            CREATE TABLE IF NOT EXISTS WorkItem (
              WorkItemId             TEXT PRIMARY KEY,
              StablePatientId        TEXT NOT NULL REFERENCES Patient(StablePatientId),
              ScheduledDate          TEXT NOT NULL,
              DeviceProfileId        TEXT NOT NULL,
              AccessionNumber        TEXT,
              RequestedProcedureId   TEXT,
              RequestedProcedureDesc TEXT,
              PatientSizeM           REAL,
              PatientWeightKg        REAL,
              SourceMessageId        TEXT,
              CreatedAtUtc           TEXT NOT NULL,
              UpdatedAtUtc           TEXT NOT NULL,
              UNIQUE (StablePatientId, ScheduledDate, DeviceProfileId)
            );

            -- StudyInstanceUID は WorkItem の upsert 時に1回だけ採番する（規則1）。
            -- UNIQUE 制約が「同じ受診者に問い合わせのたび別UIDが振られる」事故を防ぐ最後の砦。
            CREATE TABLE IF NOT EXISTS UidAllocation (
              WorkItemId       TEXT PRIMARY KEY REFERENCES WorkItem(WorkItemId),
              StudyInstanceUid TEXT NOT NULL UNIQUE,
              AllocatedAtUtc   TEXT NOT NULL
            );

            -- 1人モデル：この端末に「今」載っている受診者。常に0行か1行（規則16）。
            CREATE TABLE IF NOT EXISTS CurrentEntry (
              Singleton       INTEGER PRIMARY KEY CHECK (Singleton = 1),
              WorkItemId      TEXT NOT NULL REFERENCES WorkItem(WorkItemId),
              DeviceProfileId TEXT NOT NULL,
              SetAtUtc        TEXT NOT NULL,
              ExpiresAtUtc    TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}
