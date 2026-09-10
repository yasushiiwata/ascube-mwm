namespace Ascube.Mwm.Store.Audit;

/// <summary>
/// 監査ログ（AuditCFind/AuditCFindItem）専用のDB。ワークリストDBとは物理的に別ファイルにする。
/// 規則5（SCPはSQLiteを読み取り専用で開く。書き込む経路を作らない）は
/// CurrentEntry/WorkItem/Patient/UidAllocation（ワークリストDB）についての規則であり、
/// SCP自身が生成する運用ログである監査DBはSCPが書く（それ以外に書く主体がいない）。
/// ファイルを分けることで、監査DBへの書き込み経路がワークリストDBに触れる余地を物理的に無くす。
/// </summary>
public sealed class AuditStoreOptions
{
    public required string DatabasePath { get; set; }
}
