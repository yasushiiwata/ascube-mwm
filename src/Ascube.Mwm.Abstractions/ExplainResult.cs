namespace Ascube.Mwm.Abstractions;

/// <summary>
/// 0件になった理由を名指しで返す（実装指示書 v2 §5-T8）。
/// <see cref="Found"/> が true のとき <see cref="Reason"/> は "ok"。
/// </summary>
public sealed record ExplainResult
{
    public required bool Found { get; init; }

    public required string Reason { get; init; }
}
