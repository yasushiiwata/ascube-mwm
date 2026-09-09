namespace Ascube.Mwm.Abstractions;

/// <summary>SCP（読み取り専用）が使うもの。実装指示書 v2 §3-3。</summary>
public interface IWorklistRepository
{
    /// <summary>1人モデルなので0件か1件。IAsyncEnumerable は将来の複数人モデル用に残す。</summary>
    IAsyncEnumerable<WorkItemView> QueryAsync(QueryCriteria c, int limit, CancellationToken ct = default);

    Task<ExplainResult> ExplainAsync(QueryCriteria c, CancellationToken ct = default);
}
