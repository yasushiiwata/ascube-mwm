namespace Ascube.Mwm.Store.Audit;

/// <summary><c>mwm-admin</c> 専用の監査ログ読み取り口（実装指示書 v2 T8/T11）。</summary>
public interface IAuditReader
{
    Task<AuditCFindRecord?> GetCFindAsync(long runId, CancellationToken ct = default);

    /// <summary><c>admin health</c>（T11）用：直近のC-FIND記録（無ければ null）。</summary>
    Task<AuditCFindRecord?> GetLatestCFindAsync(CancellationToken ct = default);
}
