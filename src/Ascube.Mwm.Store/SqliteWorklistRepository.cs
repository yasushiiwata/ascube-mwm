using System.Runtime.CompilerServices;
using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Store.Internal;
using Microsoft.Data.Sqlite;

namespace Ascube.Mwm.Store;

/// <summary>
/// SCP が使う読み取り専用側。規則5（SQLite を読み取り専用で開く）を
/// Mode=ReadOnly ＋ PRAGMA query_only=1 の両方で担保する
/// （WAL の -shm に書けないと開けないため、ファイル権限での読み取り専用にはしない）。
/// </summary>
public sealed class SqliteWorklistRepository : IWorklistRepository
{
    private readonly MwmStoreOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly string _connectionString;

    public SqliteWorklistRepository(MwmStoreOptions options, TimeProvider? timeProvider = null)
    {
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
    }

    public async IAsyncEnumerable<WorkItemView> QueryAsync(QueryCriteria c, int limit, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (limit <= 0)
        {
            yield break;
        }

        var view = await TryGetLiveViewAsync(ct);
        if (view is not null)
        {
            yield return view;
        }
    }

    public async Task<ExplainResult> ExplainAsync(QueryCriteria c, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        var row = await CurrentEntryQuery.LoadAsync(connection, transaction: null, ct);

        if (row is null)
        {
            return new ExplainResult { Found = false, Reason = "CurrentEntry が存在しません（誰も設定されていません）" };
        }

        var now = _timeProvider.GetUtcNow();
        if (row.ExpiresAtUtc <= now)
        {
            return new ExplainResult
            {
                Found = false,
                Reason = $"CurrentEntry の TTL が切れています（SetAtUtc={row.SetAtUtc:O}, ExpiresAtUtc={row.ExpiresAtUtc:O}, Now={now:O}）",
            };
        }

        return new ExplainResult { Found = true, Reason = "ok" };
    }

    private async Task<WorkItemView?> TryGetLiveViewAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var row = await CurrentEntryQuery.LoadAsync(connection, transaction: null, ct);
        if (row is null || row.StudyInstanceUid is null || row.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            return null;
        }

        return new WorkItemView
        {
            WorkItemId = row.WorkItemId,
            StudyInstanceUid = row.StudyInstanceUid,
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

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA query_only = 1;";
        await pragma.ExecuteNonQueryAsync(ct);

        return connection;
    }
}
