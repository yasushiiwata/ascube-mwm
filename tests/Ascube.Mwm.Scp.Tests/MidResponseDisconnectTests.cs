using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Tools.HardClose;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Ascube.Mwm.Scp.Tests;

/// <summary>
/// 規則3・T13 E-02/E-03：「応答送信中にクライアントが一方的に切断するのは正常系」。
/// <see cref="AbruptDisconnectTests"/> はアソシエーション確立前（不完全な A-ASSOCIATE-RQ）の切断を検証しているが、
/// E-02/E-03 が指す「応答送信中」はそれとは別の局面：C-FIND の Pending 応答を実際に1件受信した直後
/// （＝最終応答 Success がまだ送られていない）に FIN/RST で強制切断するケース。
/// T11 の <c>scu find --hard-close-after</c> と同じ仕組み（<see cref="SocketCapturingNetworkManager"/>）を
/// そのまま流用し、DicomClient では実現できない「A-RELEASE を送らない強制切断」を再現する。
/// </summary>
public class MidResponseDisconnectTests
{
    private static WorkItemView MakeCandidate() => new()
    {
        WorkItemId = "wi-mid-disconnect",
        StudyInstanceUid = "2.25.111111111111111111111111111111111111",
        StablePatientId = "000012345678",
        FamilyNameKanji = "アスキューブ",
        GivenNameKanji = "タロウ",
        BirthDate = "19700101",
        Sex = Sex.Male,
        ScheduledDate = "20260910",
        RequestedProcedureDesc = "骨密度測定",
    };

    [Theory]
    [InlineData(HardCloseMode.Fin)]
    [InlineData(HardCloseMode.Rst)]
    public async Task AbruptDisconnectAfterPendingResponse_DoesNotCrashListener_NextAssociationSucceeds(HardCloseMode mode)
    {
        var repository = new FakeWorklistRepository { Current = MakeCandidate() };
        using var server = new ScpTestServer(repository, TestProfileFactory.Build());

        var services = new ServiceCollection();
        services.AddFellowOakDicom();
        services.AddSingleton<SocketCapturingNetworkManager>();
        services.AddSingleton<INetworkManager>(sp => sp.GetRequiredService<SocketCapturingNetworkManager>());
        using var clientServices = services.BuildServiceProvider();

        var networkManager = clientServices.GetRequiredService<SocketCapturingNetworkManager>();
        var client = clientServices.GetRequiredService<IDicomClientFactory>()
            .Create("127.0.0.1", server.Port, false, "ANY_SCU", "ASCUBE_MWM");

        var request = new DicomCFindRequest(DicomUID.ModalityWorklistInformationModelFind) { Dataset = new DicomDataset() };
        var pendingReceived = false;
        request.OnResponseReceived += (_, response) =>
        {
            if (response.Status == DicomStatus.Pending && !pendingReceived)
            {
                pendingReceived = true;
                // Success（最終応答）がまだ送られていない状態で、A-RELEASE を送らずに TCP を強制切断する。
                networkManager.HardClose(mode);
            }
        };

        try
        {
            await client.AddRequestAsync(request);
            await client.SendAsync();
        }
        catch
        {
            // 強制切断後の送受信で例外が出るのは想定どおり（目的は SCP 側が生き残ることの確認）。
        }

        Assert.True(pendingReceived, "テスト前提が崩れている：Pending 応答を受信できなかった。");

        await Task.Delay(300);

        Assert.True(server.Server.IsListening);
        Assert.Equal(DicomStatus.Success, await RunEchoAsync(server.Port));
    }

    private static async Task<DicomStatus?> RunEchoAsync(int port)
    {
        var client = DicomClientFactory.Create("127.0.0.1", port, false, "ANY_SCU", "ASCUBE_MWM");
        DicomStatus? status = null;
        var request = new DicomCEchoRequest();
        request.OnResponseReceived += (_, response) => status = response.Status;

        await client.AddRequestAsync(request);
        await client.SendAsync();

        return status;
    }
}
