using System.Text.Encodings.Web;
using System.Text.Json;

namespace Ascube.Mwm.Core.Config;

/// <summary>
/// 旧→新プロファイルの差分を人間可読な行の集合で表す（実装指示書 v2 T10：「旧→新の差分をログに出す」）。
/// マージ済み JSON（<see cref="DeviceProfile.Raw"/>）をインデント付きで直列化し、行単位の集合差分を取る
/// （厳密な構造比較ではないが、実運用でよくある「1〜数行だけ変わる」編集には十分実用的）。
/// </summary>
public static class ProfileDiffFormatter
{
    public static IReadOnlyList<string> Format(DeviceProfile before, DeviceProfile after)
    {
        var beforeLines = ToLines(before);
        var afterLines = ToLines(after);

        var removed = beforeLines.Except(afterLines).Select(l => $"- {l}");
        var added = afterLines.Except(beforeLines).Select(l => $"+ {l}");

        return removed.Concat(added).ToArray();
    }

    // 日本語（説明文等）が \uXXXX にエスケープされてログが読めなくなるのを避ける。
    // このJSONはログ表示・バックアップファイル専用で、HTML等に埋め込むことは無いため安全側に振れる必要はない。
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static HashSet<string> ToLines(DeviceProfile profile)
    {
        var json = JsonSerializer.Serialize(profile.Raw, SerializerOptions);
        return json
            .Split('\n')
            .Select(l => l.TrimEnd('\r').Trim())
            .Where(l => l.Length > 0)
            .ToHashSet();
    }
}
