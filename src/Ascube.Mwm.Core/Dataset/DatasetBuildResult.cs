using FellowOakDicom;

namespace Ascube.Mwm.Core.Dataset;

/// <summary>
/// <see cref="DatasetBuilder"/> の結果。<see cref="Suppressed"/> が true のとき、
/// この行は C-FIND 応答に含めない（規則2・規則4の合わせ技：0件＋Success として扱う）。
/// </summary>
public sealed class DatasetBuildResult
{
    public required bool Suppressed { get; init; }

    public string? SuppressedReason { get; init; }

    public DicomDataset? Dataset { get; init; }

    /// <summary>来歴。タグのDICOM表記文字列（SQ内は "(tag)[index].(tag)" 形式）→ 実際に使われた source 式。</summary>
    public IReadOnlyDictionary<string, string> Provenance { get; init; } = new Dictionary<string, string>();
}
