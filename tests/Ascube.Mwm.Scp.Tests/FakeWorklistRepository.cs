using System.Runtime.CompilerServices;
using Ascube.Mwm.Abstractions;

namespace Ascube.Mwm.Scp.Tests;

/// <summary>
/// SqliteWorklistRepository と同じ判定（MatchEngine）をインメモリで行うテスト用の偽実装。
/// SCP 側（MwmDicomService）の C-FIND オーケストレーションだけをテストしたいときに使う
/// （MatchEngine 自体の判定ロジックは Core.Tests、SQLite 永続化は Store.Tests が担当）。
/// </summary>
internal sealed class FakeWorklistRepository : IWorklistRepository
{
    public WorkItemView? Current { get; set; }

    public async IAsyncEnumerable<WorkItemView> QueryAsync(QueryCriteria c, int limit, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (limit > 0 && Current is not null && MatchEngine.Evaluate(c, Current).IsMatch)
        {
            yield return Current;
        }

        await Task.CompletedTask;
    }

    public Task<ExplainResult> ExplainAsync(QueryCriteria c, CancellationToken ct = default)
    {
        if (Current is null)
        {
            return Task.FromResult(new ExplainResult { Found = false, Reason = "CurrentEntry が存在しません" });
        }

        var match = MatchEngine.Evaluate(c, Current);
        if (!match.IsMatch)
        {
            var reason = match.Outcome switch
            {
                MatchOutcome.ScheduledDateOutOfRange =>
                    $"ScheduledDate が要求範囲外です（要求: \"{c.ScheduledDateRange}\", 保持: \"{Current.ScheduledDate}\"）",
                MatchOutcome.PatientIdMismatch =>
                    $"PatientID の指名が一致しません（要求: \"{c.PatientId}\", 保持: \"{Current.StablePatientId}\"）",
                MatchOutcome.PatientNameMismatch =>
                    $"PatientName の指名が一致しません（要求: \"{c.PatientName}\"）",
                _ => "0件です（詳細不明）",
            };
            return Task.FromResult(new ExplainResult { Found = false, Reason = reason });
        }

        return Task.FromResult(new ExplainResult { Found = true, Reason = "ok" });
    }
}
