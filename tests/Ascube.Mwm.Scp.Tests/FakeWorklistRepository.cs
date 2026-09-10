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

    public Task<ExplainResult> ExplainAsync(QueryCriteria c, CancellationToken ct = default) =>
        Task.FromResult(Current is not null
            ? new ExplainResult { Found = true, Reason = "ok" }
            : new ExplainResult { Found = false, Reason = "CurrentEntry が存在しません" });
}
