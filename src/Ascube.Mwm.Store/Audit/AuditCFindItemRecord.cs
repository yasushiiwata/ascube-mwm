namespace Ascube.Mwm.Store.Audit;

/// <summary>
/// AuditCFind 1件が対象にした候補1件の来歴（実装指示書 v2 T8）。1人モデルでは高々1件。
/// <see cref="SuppressedReason"/> が非nullなら、その候補は最終的に返されなかった（規則2・規則4）。
/// </summary>
public sealed record AuditCFindItemRecord
{
    public required int ItemIndex { get; init; }

    /// <summary>タグ→実際に使われた source 式、の JSON（DatasetBuilder の Provenance）。返された行にのみ設定。</summary>
    public string? ProvenanceJson { get; init; }

    public string? SuppressedReason { get; init; }
}
