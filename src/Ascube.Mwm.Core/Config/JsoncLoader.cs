using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ascube.Mwm.Core.Config;

/// <summary>JSONC（コメント・末尾カンマ許容）プロファイルの読込と、_base.jsonc との深いマージ。</summary>
public static class JsoncLoader
{
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = false };

    private static readonly JsonDocumentOptions DocOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static JsonObject Parse(string jsonc, string sourceName)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(jsonc, NodeOptions, DocOptions);
        }
        catch (JsonException ex)
        {
            throw new ProfileFormatException(sourceName, $"JSON 構文エラー: {ex.Message}");
        }

        if (node is not JsonObject obj)
        {
            throw new ProfileFormatException(sourceName, "ルートは JSON オブジェクトである必要があります");
        }

        return obj;
    }

    /// <summary>_base.jsonc に指定プロファイルを重ねてマージした結果を返す。profileId が "_base" ならそのまま返す。</summary>
    public static JsonObject LoadMerged(string profileId, string profilesDir)
    {
        var basePath = Path.Combine(profilesDir, "_base.jsonc");
        if (!File.Exists(basePath))
        {
            throw new ProfileFormatException(basePath, "_base.jsonc が見つかりません");
        }

        var baseObj = Parse(File.ReadAllText(basePath), basePath);
        if (profileId == "_base")
        {
            return baseObj;
        }

        var profilePath = Path.Combine(profilesDir, $"{profileId}.jsonc");
        if (!File.Exists(profilePath))
        {
            throw new ProfileFormatException(profilePath, $"プロファイルが見つかりません: {profileId}");
        }

        var overlay = Parse(File.ReadAllText(profilePath), profilePath);
        return DeepMerge(baseObj, overlay);
    }

    /// <summary>オブジェクトはキーごとに再帰マージ、配列とスカラーは overlay の値で置き換える。</summary>
    public static JsonObject DeepMerge(JsonObject baseObj, JsonObject overlay)
    {
        var result = baseObj.DeepClone()!.AsObject();

        foreach (var (key, overlayValue) in overlay)
        {
            if (overlayValue is JsonObject overlayChild && result[key] is JsonObject baseChild)
            {
                result[key] = DeepMerge(baseChild, overlayChild);
            }
            else
            {
                result[key] = overlayValue?.DeepClone();
            }
        }

        return result;
    }
}
