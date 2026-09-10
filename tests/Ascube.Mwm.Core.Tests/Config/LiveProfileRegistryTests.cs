using Ascube.Mwm.Core.Config;

namespace Ascube.Mwm.Core.Tests.Config;

/// <summary>T10：ホットリロード＋自動ロールバック。</summary>
public class LiveProfileRegistryTests
{
    private const string BaseJson = """
        {
          "schemaVersion": 1,
          "network": { "aeTitle": "TEST", "port": 11112, "callingAeMatching": "ignore",
            "acceptedTransferSyntaxes": ["1.2.840.10008.1.2"] },
          "matching": { "modalityMatching": "ignore", "maxResults": 1 },
          "visibility": { "mode": "current-only", "currentTtlMinutes": 15 },
          "charset": { "specificCharacterSet": "ISO_IR 192" },
          "dataset": { "elements": [] }
        }
        """;

    private static string CreateProfilesDir(string profileJson)
    {
        var dir = Directory.CreateTempSubdirectory("ascube-mwm-hotreload-tests-").FullName;
        File.WriteAllText(Path.Combine(dir, "_base.jsonc"), BaseJson);
        File.WriteAllText(Path.Combine(dir, "TEST.jsonc"), profileJson);
        return dir;
    }

    [Fact]
    public void TryReload_ValidChange_SwapsCurrentAndReportsSuccess()
    {
        var dir = CreateProfilesDir("""{ "id": "TEST", "charset": { "specificCharacterSet": "ISO_IR 192" } }""");
        var initial = ProfileLoader.LoadAndValidate("TEST", dir).Profile!;
        var registry = new LiveProfileRegistry([initial]);

        File.WriteAllText(Path.Combine(dir, "TEST.jsonc"), """{ "id": "TEST", "charset": { "specificCharacterSet": "ISO_IR 13" } }""");

        var result = registry.TryReload(["TEST"], dir);

        Assert.True(result.Success);
        Assert.Equal("ISO_IR 13", registry.Current.Single().SpecificCharacterSet);
        Assert.Equal("ISO_IR 192", result.Previous.Single().SpecificCharacterSet);
        Assert.Equal("ISO_IR 13", result.Applied!.Single().SpecificCharacterSet);
    }

    [Fact]
    public void TryReload_InvalidChange_KeepsRunningConfig()
    {
        var dir = CreateProfilesDir("""{ "id": "TEST", "charset": { "specificCharacterSet": "ISO_IR 192" } }""");
        var initial = ProfileLoader.LoadAndValidate("TEST", dir).Profile!;
        var registry = new LiveProfileRegistry([initial]);

        // 規則15違反（ISO 2022 系）を仕込んで検証失敗を起こす（=設定破壊テスト。L-03相当）。
        File.WriteAllText(Path.Combine(dir, "TEST.jsonc"), """{ "id": "TEST", "charset": { "specificCharacterSet": "ISO 2022 IR 87" } }""");

        var result = registry.TryReload(["TEST"], dir);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Issues);
        Assert.Equal("ISO_IR 192", registry.Current.Single().SpecificCharacterSet); // 稼働中の設定を維持
    }

    [Fact]
    public void TryReload_MultipleProfiles_OneInvalid_RejectsAllAndKeepsPrevious()
    {
        var dir = Directory.CreateTempSubdirectory("ascube-mwm-hotreload-tests-").FullName;
        File.WriteAllText(Path.Combine(dir, "_base.jsonc"), BaseJson);
        File.WriteAllText(Path.Combine(dir, "A.jsonc"), """{ "id": "A", "network": { "aeTitle": "A_AE" } }""");
        File.WriteAllText(Path.Combine(dir, "B.jsonc"), """{ "id": "B", "network": { "aeTitle": "B_AE" } }""");

        var a = ProfileLoader.LoadAndValidate("A", dir).Profile!;
        var b = ProfileLoader.LoadAndValidate("B", dir).Profile!;
        var registry = new LiveProfileRegistry([a, b]);

        // B だけ壊す。
        File.WriteAllText(Path.Combine(dir, "B.jsonc"), """{ "id": "B", "charset": { "specificCharacterSet": "ISO 2022 IR 87" } }""");

        var result = registry.TryReload(["A", "B"], dir);

        Assert.False(result.Success);
        // A は壊れていないのに、B が壊れているので全体を差し替えない（部分適用しない）。
        Assert.Equal(2, registry.Current.Count);
        Assert.Equal("A_AE", registry.Current.First(p => p.Id == "A").AeTitle);
    }

    [Fact]
    public void TryReload_Success_ReportsPreviousAndApplied_ForDiffing()
    {
        var dir = CreateProfilesDir("""{ "id": "TEST", "network": { "aeTitle": "TEST" }, "charset": { "specificCharacterSet": "ISO_IR 192" } }""");
        var initial = ProfileLoader.LoadAndValidate("TEST", dir).Profile!;
        var registry = new LiveProfileRegistry([initial]);

        File.WriteAllText(Path.Combine(dir, "TEST.jsonc"), """{ "id": "TEST", "network": { "aeTitle": "TEST" }, "charset": { "specificCharacterSet": "ISO_IR 192", "patientName": { "group1": "kanaFull" } } }""");
        var result = registry.TryReload(["TEST"], dir);

        Assert.True(result.Success);
        var diff = ProfileDiffFormatter.Format(result.Previous.Single(), result.Applied!.Single());
        Assert.Contains(diff, l => l.StartsWith('+') && l.Contains("kanaFull"));
    }
}
