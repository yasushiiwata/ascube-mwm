using System.Text.Json.Nodes;
using Ascube.Mwm.Core.Config;

namespace Ascube.Mwm.Scp.Tests;

/// <summary>T3 のテスト用に最小限の DeviceProfile を組み立てる（ProfileValidator を通してから DeviceProfile 化する）。</summary>
internal static class TestProfileFactory
{
    public static readonly string[] StandardTransferSyntaxes =
    [
        "1.2.840.10008.1.2",
        "1.2.840.10008.1.2.1",
        "1.2.840.10008.1.2.2",
    ];

    public static DeviceProfile Build(
        string id = "TEST",
        string aeTitle = "ASCUBE_MWM",
        string callingAeMatching = "ignore",
        string[]? allowedCallingAeTitles = null,
        string[]? acceptedTransferSyntaxes = null)
    {
        var network = new JsonObject
        {
            ["aeTitle"] = aeTitle,
            ["port"] = 11112,
            ["callingAeMatching"] = callingAeMatching,
            ["acceptedTransferSyntaxes"] = new JsonArray((acceptedTransferSyntaxes ?? StandardTransferSyntaxes)
                .Select(u => (JsonNode)u).ToArray()),
        };

        if (allowedCallingAeTitles is { Length: > 0 })
        {
            network["allowedCallingAeTitles"] = new JsonArray(allowedCallingAeTitles.Select(a => (JsonNode)a).ToArray());
        }

        var profile = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["network"] = network,
            ["matching"] = new JsonObject { ["modalityMatching"] = "ignore", ["maxResults"] = 1 },
            ["visibility"] = new JsonObject { ["mode"] = "current-only", ["currentTtlMinutes"] = 15 },
            ["charset"] = new JsonObject { ["specificCharacterSet"] = "ISO_IR 192" },
            ["dataset"] = new JsonObject { ["elements"] = new JsonArray() },
        };

        var validation = ProfileValidator.Validate(profile);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                "テスト用プロファイルの組み立てに失敗: " + string.Join("; ", validation.Issues.Select(i => $"{i.Path}: {i.Message}")));
        }

        return DeviceProfile.FromValidated(id, profile);
    }
}
