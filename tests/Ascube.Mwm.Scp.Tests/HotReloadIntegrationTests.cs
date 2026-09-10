using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Core.Config;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace Ascube.Mwm.Scp.Tests;

/// <summary>
/// T10：ホットリロードが SCP の実際のアソシエーション処理に効くことを確認する
/// （ファイル監視・デバウンス自体は Core.Tests の LiveProfileRegistryTests が担当。ここでは
/// LiveProfileRegistry.TryReload を直接呼んで「差し替え後の新しいアソシエーションに新設定が効く」ことを見る）。
/// </summary>
public class HotReloadIntegrationTests
{
    private const string BaseJson = """
        {
          "schemaVersion": 1,
          "network": { "aeTitle": "TEST", "port": 11112, "callingAeMatching": "ignore",
            "acceptedTransferSyntaxes": ["1.2.840.10008.1.2", "1.2.840.10008.1.2.1", "1.2.840.10008.1.2.2"] },
          "matching": { "modalityMatching": "ignore", "maxResults": 1 },
          "visibility": { "mode": "current-only", "currentTtlMinutes": 15 },
          "charset": { "specificCharacterSet": "ISO_IR 192" },
          "dataset": { "elements": [] }
        }
        """;

    private static string CreateProfilesDir(string charsetJson)
    {
        var dir = Directory.CreateTempSubdirectory("ascube-mwm-hotreload-scp-tests-").FullName;
        File.WriteAllText(Path.Combine(dir, "_base.jsonc"), BaseJson);
        File.WriteAllText(Path.Combine(dir, "TEST.jsonc"), $$"""{ "id": "TEST", "charset": { "specificCharacterSet": "{{charsetJson}}" } }""");
        return dir;
    }

    [Fact]
    public async Task AfterSuccessfulReload_NewAssociation_SeesNewCharset()
    {
        var dir = CreateProfilesDir("ISO_IR 192");
        var initial = ProfileLoader.LoadAndValidate("TEST", dir).Profile!;
        using var server = new ScpTestServer(initial);

        // 稼働中に案④相当（ISO_IR 13）へ切り替える。
        File.WriteAllText(Path.Combine(dir, "TEST.jsonc"), """{ "id": "TEST", "charset": { "specificCharacterSet": "ISO_IR 13" } }""");
        var reload = server.ProfileRegistry.TryReload(["TEST"], dir);
        Assert.True(reload.Success);

        var client = DicomClientFactory.Create("127.0.0.1", server.Port, false, "ANY_SCU", "TEST");
        DicomStatus? status = null;
        var request = new DicomCEchoRequest();
        request.OnResponseReceived += (_, r) => status = r.Status;
        await client.AddRequestAsync(request);
        await client.SendAsync();

        Assert.Equal(DicomStatus.Success, status);
        Assert.Equal("ISO_IR 13", server.ProfileRegistry.Current.Single().SpecificCharacterSet);
    }

    [Fact]
    public async Task FailedReload_KeepsServingWithPreviousConfig()
    {
        var dir = CreateProfilesDir("ISO_IR 192");
        var initial = ProfileLoader.LoadAndValidate("TEST", dir).Profile!;
        using var server = new ScpTestServer(initial);

        // 規則15違反（ISO 2022系）を仕込んで検証失敗を起こす。
        File.WriteAllText(Path.Combine(dir, "TEST.jsonc"), """{ "id": "TEST", "charset": { "specificCharacterSet": "ISO 2022 IR 87" } }""");
        var reload = server.ProfileRegistry.TryReload(["TEST"], dir);
        Assert.False(reload.Success);

        // 稼働中の設定（ISO_IR 192）のままアソシエーションが成立し続けること。
        var client = DicomClientFactory.Create("127.0.0.1", server.Port, false, "ANY_SCU", "TEST");
        DicomStatus? status = null;
        var request = new DicomCEchoRequest();
        request.OnResponseReceived += (_, r) => status = r.Status;
        await client.AddRequestAsync(request);
        await client.SendAsync();

        Assert.Equal(DicomStatus.Success, status);
        Assert.Equal("ISO_IR 192", server.ProfileRegistry.Current.Single().SpecificCharacterSet);
    }

    [Fact]
    public async Task InFlightAssociation_KeepsUsingSnapshotFromAssociationTime_EvenAfterReloadMidAssociation()
    {
        // T10：「進行中のアソシエーションは読込時のスナップショットを使い続ける」。
        var dir = CreateProfilesDir("ISO_IR 192");
        var initial = ProfileLoader.LoadAndValidate("TEST", dir).Profile!;
        var candidate = new WorkItemView
        {
            WorkItemId = "wi-1",
            StudyInstanceUid = "2.25.1",
            StablePatientId = "000012345678",
            FamilyNameKanji = "武田",
            GivenNameKanji = "太郎",
            ScheduledDate = "20260910",
        };
        var repository = new FakeWorklistRepository { Current = candidate };
        using var server = new ScpTestServer(repository, initial);

        var client = DicomClientFactory.Create("127.0.0.1", server.Port, false, "ANY_SCU", "TEST");

        var request1 = new DicomCFindRequest(DicomUID.ModalityWorklistInformationModelFind);
        string? charsetFromFirstResponse = null;
        request1.OnResponseReceived += (_, r) =>
        {
            if (r.Status == DicomStatus.Pending)
            {
                charsetFromFirstResponse = r.Dataset!.GetString(DicomTag.SpecificCharacterSet);
            }

            // 1件目の応答を受け取った直後（＝アソシエーション確立後・2件目送信前）に稼働中設定を切り替える。
            File.WriteAllText(Path.Combine(dir, "TEST.jsonc"), """{ "id": "TEST", "charset": { "specificCharacterSet": "ISO_IR 13" } }""");
            server.ProfileRegistry.TryReload(["TEST"], dir);
        };

        var request2 = new DicomCFindRequest(DicomUID.ModalityWorklistInformationModelFind);
        string? charsetFromSecondResponse = null;
        request2.OnResponseReceived += (_, r) =>
        {
            if (r.Status == DicomStatus.Pending)
            {
                charsetFromSecondResponse = r.Dataset!.GetString(DicomTag.SpecificCharacterSet);
            }
        };

        await client.AddRequestAsync(request1);
        await client.AddRequestAsync(request2);
        await client.SendAsync();

        // レジストリ自体はもう新設定（ISO_IR 13）になっている。
        Assert.Equal("ISO_IR 13", server.ProfileRegistry.Current.Single().SpecificCharacterSet);

        // それでも同一アソシエーション内の両方の応答は、アソシエーション確立時のスナップショット（ISO_IR 192）のまま。
        Assert.Equal("ISO_IR 192", charsetFromFirstResponse);
        Assert.Equal("ISO_IR 192", charsetFromSecondResponse);
    }
}
