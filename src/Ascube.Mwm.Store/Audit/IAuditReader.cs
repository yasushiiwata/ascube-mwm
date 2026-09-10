namespace Ascube.Mwm.Store.Audit;

/// <summary><c>mwm-admin explain-query</c> 専用の監査ログ読み取り口（実装指示書 v2 T8）。</summary>
public interface IAuditReader
{
    Task<AuditCFindRecord?> GetCFindAsync(long runId, CancellationToken ct = default);
}
