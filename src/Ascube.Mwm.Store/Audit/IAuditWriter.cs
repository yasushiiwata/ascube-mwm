namespace Ascube.Mwm.Store.Audit;

/// <summary>SCP 専用の監査ログ書き込み口（実装指示書 v2 T8）。</summary>
public interface IAuditWriter
{
    /// <summary>1件記録し、DB が採番した RunId を返す。</summary>
    Task<long> RecordCFindAsync(AuditCFindRecord record, CancellationToken ct = default);
}
