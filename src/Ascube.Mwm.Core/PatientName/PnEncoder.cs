using Ascube.Mwm.Abstractions;

namespace Ascube.Mwm.Core.PatientName;

/// <summary>
/// プロファイルの charset.patientName 群割当から PN 値を組み立て、生バイトを直接検査する
/// （実装指示書 v2 T7）。代替文字で無言に化けさせない：検査に落ちたら <see cref="PnEncodeResult.Suppressed"/>。
/// </summary>
public static class PnEncoder
{
    public static PnEncodeResult Encode(PnGroupAssignment assignment, WorkItemView item, string specificCharacterSet)
    {
        var groups = new[]
        {
            ResolveGroup(assignment.Group1, item),
            ResolveGroup(assignment.Group2, item),
            ResolveGroup(assignment.Group3, item),
        };

        var lastNonEmpty = Array.FindLastIndex(groups, g => g is not null);
        if (lastNonEmpty < 0)
        {
            return new PnEncodeResult(Suppressed: true, Value: null, Reason: "全ての群が未解決のため PN を構成できません");
        }

        var value = string.Join("=", groups.Take(lastNonEmpty + 1).Select(g => g ?? string.Empty));

        return specificCharacterSet switch
        {
            "ISO_IR 13" => ValidateAndReturn(value, PnByteValidator.EncodeAsIsoIr13Bytes(value), PnByteValidator.IsValidIsoIr13, "ISO_IR 13"),
            "ISO_IR 192" => ValidateAndReturn(value, PnByteValidator.EncodeAsUtf8Bytes(value), PnByteValidator.IsValidUtf8, "ISO_IR 192"),
            _ => throw new NotSupportedException(
                $"未対応の specificCharacterSet です（ProfileValidator を通過しているはずなのに。規則15により使えるのは ISO_IR 192 / ISO_IR 13 のみ）: {specificCharacterSet}"),
        };
    }

    private static PnEncodeResult ValidateAndReturn(string value, byte[] bytes, Func<byte[], bool> isValid, string charsetLabel) =>
        isValid(bytes)
            ? new PnEncodeResult(Suppressed: false, Value: value, Reason: null)
            : new PnEncodeResult(Suppressed: true, Value: null, Reason: $"PN の生バイトが {charsetLabel} として不正です（規則19：生バイトを直接検査。代替文字で無言に化けさせない）");

    private static string? ResolveGroup(PnGroupKind kind, WorkItemView item) => kind switch
    {
        PnGroupKind.None => null,
        PnGroupKind.Kanji => Compose(item.FamilyNameKanji, item.GivenNameKanji),
        PnGroupKind.KanaHalf => Compose(item.FamilyNameKana, item.GivenNameKana),
        PnGroupKind.KanaFull => Compose(
            item.FamilyNameKana is null ? null : HalfToFullWidthKana.Convert(item.FamilyNameKana),
            item.GivenNameKana is null ? null : HalfToFullWidthKana.Convert(item.GivenNameKana)),
        PnGroupKind.Romaji => null, // WorkItemView にローマ字表記の列が無いため未実装
        _ => null,
    };

    private static string? Compose(string? family, string? given) =>
        string.IsNullOrEmpty(family) && string.IsNullOrEmpty(given) ? null : $"{family}^{given}";
}
