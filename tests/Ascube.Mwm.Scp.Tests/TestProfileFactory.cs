using System.Text.Json.Nodes;
using Ascube.Mwm.Core.Config;

namespace Ascube.Mwm.Scp.Tests;

/// <summary>
/// テスト用の DeviceProfile を組み立てる（ProfileValidator を通してから DeviceProfile 化する）。
/// dataset.elements は config/profiles/_base.jsonc と同じ形（T6 DatasetBuilder のテストが実データで組み立てられるように）。
/// </summary>
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
            ["charset"] = new JsonObject
            {
                ["specificCharacterSet"] = "ISO_IR 192",
                ["patientName"] = new JsonObject { ["group1"] = "kanaFull", ["group2"] = "kanji", ["group3"] = "kanaFull" },
            },
            ["dataset"] = JsonNode.Parse(DatasetElementsJson)!,
        };

        var validation = ProfileValidator.Validate(profile);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                "テスト用プロファイルの組み立てに失敗: " + string.Join("; ", validation.Issues.Select(i => $"{i.Path}: {i.Message}")));
        }

        return DeviceProfile.FromValidated(id, profile);
    }

    // config/profiles/_base.jsonc の dataset.elements と同じ形。
    private const string DatasetElementsJson = """
        {
          "elements": [
            { "tag": "(0010,0010)", "vr": "PN", "vm": "1", "source": "auto:patientName", "required": true, "onMissing": "reject-item" },
            { "tag": "(0010,0020)", "vr": "LO", "vm": "1", "source": "db:StablePatientId", "required": true, "onMissing": "reject-item", "maxLength": 64 },
            { "tag": "(0010,0030)", "vr": "DA", "vm": "1", "source": "db:BirthDate", "required": false, "onMissing": "empty" },
            { "tag": "(0010,0040)", "vr": "CS", "vm": "1", "source": "auto:patientSex", "required": false, "onMissing": "empty" },
            { "tag": "(0010,1020)", "vr": "DS", "vm": "1", "source": "db:PatientSizeM", "required": false, "onMissing": "omit" },
            { "tag": "(0010,1030)", "vr": "DS", "vm": "1", "source": "db:PatientWeightKg", "required": false, "onMissing": "omit" },
            { "tag": "(0020,000D)", "vr": "UI", "vm": "1", "source": "uid:study", "required": true, "onMissing": "reject-item" },
            { "tag": "(0008,0050)", "vr": "SH", "vm": "1", "source": "db:AccessionNumber", "required": false, "onMissing": "empty", "maxLength": 16 },
            { "tag": "(0040,1001)", "vr": "SH", "vm": "1", "source": "db:RequestedProcedureId", "required": false, "onMissing": "empty" },
            { "tag": "(0032,1060)", "vr": "LO", "vm": "1", "source": "db:RequestedProcedureDesc", "required": false, "onMissing": "empty" },
            {
              "tag": "(0040,0100)", "vr": "SQ", "required": true, "onMissing": "reject-item",
              "items": [
                {
                  "elements": [
                    { "tag": "(0008,0060)", "vr": "CS", "vm": "1", "source": "const:", "required": false, "onMissing": "empty" },
                    { "tag": "(0040,0001)", "vr": "AE", "vm": "1", "source": "const:ASCUBE_MWM", "required": false, "onMissing": "empty" },
                    { "tag": "(0040,0002)", "vr": "DA", "vm": "1", "source": "db:ScheduledDate", "required": true, "onMissing": "reject-item" },
                    { "tag": "(0040,0003)", "vr": "TM", "vm": "1", "source": "const:", "required": false, "onMissing": "empty" },
                    { "tag": "(0040,0007)", "vr": "LO", "vm": "1", "source": "db:RequestedProcedureDesc", "required": false, "onMissing": "empty" }
                  ]
                }
              ]
            }
          ]
        }
        """;
}
