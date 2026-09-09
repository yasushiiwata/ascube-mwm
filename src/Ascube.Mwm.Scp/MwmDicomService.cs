using System.Linq;
using System.Text;
using Ascube.Mwm.Core.Config;
using FellowOakDicom;
using FellowOakDicom.Network;
using Microsoft.Extensions.Logging;

namespace Ascube.Mwm.Scp;

/// <summary>
/// C-ECHO SCP ＋ アソシエーション制御（実装指示書 v2 T3）＋ C-FIND 最小実装（T4）。
/// フロー：ルーティングでプロファイル解決 → 解決結果をログ出力 → Calling AE 照合 → PC ごとに accept/reject。
/// C-FIND は現時点ではハードコードした1件を返すのみ（実データ照合は T5 MatchEngine、
/// データセット組み立ては T6 DatasetBuilder で置き換える）。
/// </summary>
public sealed class MwmDicomService : DicomService, IDicomServiceProvider, IDicomCEchoProvider, IDicomCFindProvider
{
    private static readonly DicomUID[] SupportedAbstractSyntaxes =
    [
        DicomUID.Verification,
        DicomUID.ModalityWorklistInformationModelFind,
    ];

    public MwmDicomService(INetworkStream stream, Encoding fallbackEncoding, ILogger logger, DicomServiceDependencies dependencies)
        : base(stream, fallbackEncoding, logger, dependencies)
    {
    }

    private ScpRoutingContext Routing =>
        UserState as ScpRoutingContext
        ?? throw new InvalidOperationException($"{nameof(UserState)} に {nameof(ScpRoutingContext)} が設定されていません。IDicomServerFactory.Create の呼び出しを確認してください。");

    public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        var calledAe = association.CalledAE;
        var profile = Routing.ResolveByCalledAe(calledAe);

        if (profile is null)
        {
            Logger.LogWarning(
                "アソシエーション拒否（規則10：理由コード付きで応答、黙って切らない）: Called AE Title \"{CalledAe}\" に一致するプロファイルがありません。Calling AE=\"{CallingAe}\" Remote={RemoteHost}:{RemotePort}",
                calledAe, association.CallingAE, association.RemoteHost, association.RemotePort);
            return SendAssociationRejectAsync(DicomRejectResult.Permanent, DicomRejectSource.ServiceUser, DicomRejectReason.CalledAENotRecognized);
        }

        Logger.LogInformation(
            "ルーティング解決: Called AE \"{CalledAe}\" → プロファイル \"{ProfileId}\"（Calling AE=\"{CallingAe}\" Remote={RemoteHost}:{RemotePort}）",
            calledAe, profile.Id, association.CallingAE, association.RemoteHost, association.RemotePort);

        if (!IsCallingAeAccepted(profile, association.CallingAE, out var shouldReject))
        {
            if (shouldReject)
            {
                Logger.LogWarning(
                    "アソシエーション拒否（規則10）: Calling AE Title \"{CallingAe}\" はプロファイル \"{ProfileId}\" の許可リストにありません（callingAeMatching=strict）",
                    association.CallingAE, profile.Id);
                return SendAssociationRejectAsync(DicomRejectResult.Permanent, DicomRejectSource.ServiceUser, DicomRejectReason.CallingAENotRecognized);
            }

            Logger.LogWarning(
                "Calling AE Title \"{CallingAe}\" はプロファイル \"{ProfileId}\" の許可リストにありません（callingAeMatching=logOnly のため接続は継続）",
                association.CallingAE, profile.Id);
        }

        var acceptedTransferSyntaxes = profile.AcceptedTransferSyntaxUids
            .Select(DicomTransferSyntax.Parse)
            .ToArray();

        // 規則9：未サポートの PC だけを reject し、アソシエーション自体は成立させる。
        foreach (var pc in association.PresentationContexts)
        {
            if (SupportedAbstractSyntaxes.Contains(pc.AbstractSyntax))
            {
                pc.AcceptTransferSyntaxes(acceptedTransferSyntaxes);
            }
            else
            {
                pc.SetResult(DicomPresentationContextResult.RejectAbstractSyntaxNotSupported);
            }
        }

        return SendAssociationAcceptAsync(association);
    }

    /// <summary>
    /// Calling AE の照合。<paramref name="shouldReject"/> は戻り値が false のときだけ意味を持ち、
    /// true なら strict（アソシエーションを拒否すべき）、false なら logOnly（ログのみで続行）。
    /// </summary>
    private static bool IsCallingAeAccepted(DeviceProfile profile, string callingAe, out bool shouldReject)
    {
        shouldReject = false;

        if (profile.CallingAeMatching == "ignore")
        {
            return true;
        }

        var trimmed = callingAe.Trim();
        var matched = profile.AllowedCallingAeTitles.Any(ae => string.Equals(ae, trimmed, StringComparison.Ordinal));
        if (matched)
        {
            return true;
        }

        shouldReject = profile.CallingAeMatching == "strict";
        return false;
    }

    public Task OnReceiveAssociationReleaseRequestAsync() => SendAssociationReleaseResponseAsync();

    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason)
    {
        Logger.LogInformation("アソシエーション Abort 受信: source={Source} reason={Reason}", source, reason);
    }

    /// <summary>
    /// 規則3：応答送信中にクライアントが一方的に切断するのは正常系。ここで例外を再スローすると
    /// リスナ全体が道連れになり「二度と繋がらない」状態になる。FIN・RST いずれでも再スローしない。
    /// </summary>
    public void OnConnectionClosed(Exception? exception)
    {
        if (exception is not null)
        {
            Logger.LogInformation(exception, "接続がクライアント側から切断されました（正常系。規則3により再スローしない）");
        }
    }

    public Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request)
        => Task.FromResult(new DicomCEchoResponse(request, DicomStatus.Success));

    /// <summary>
    /// T4：C-FIND 最小実装。実データの照合（T5 MatchEngine）・実データからの組み立て（T6 DatasetBuilder）は
    /// まだ無く、ハードコードした1件を Pending で返してから Success を返すだけ。
    /// ここで得た pcap を以後の「正解サンプル」とする（実装指示書 v2 T4）。
    /// </summary>
    public async IAsyncEnumerable<DicomCFindResponse> OnCFindRequestAsync(DicomCFindRequest request)
    {
        var response = new DicomCFindResponse(request, DicomStatus.Pending)
        {
            Dataset = BuildFixedWorklistDataset(),
        };
        yield return response;

        yield return new DicomCFindResponse(request, DicomStatus.Success);
        await Task.CompletedTask;
    }

    private static DicomDataset BuildFixedWorklistDataset()
    {
        var dataset = new DicomDataset();

        // 規則6：SpecificCharacterSet は DicomDataset に最初に設定する。
        dataset.Add(DicomTag.SpecificCharacterSet, "ISO_IR 192");

        // T4 時点ではハードコード値（実データではない）。T5/T6 で実データ経由の生成に置き換える。
        dataset.Add(DicomTag.PatientName, "アスキューブ^タロウ");
        dataset.Add(DicomTag.PatientID, "000012345678");
        dataset.Add(DicomTag.PatientBirthDate, "19700101");
        dataset.Add(DicomTag.PatientSex, "M");
        dataset.Add(DicomTag.StudyInstanceUID, "2.25.100000000000000000000000000000000001");
        dataset.Add(DicomTag.AccessionNumber, "A0000001");
        dataset.Add(DicomTag.RequestedProcedureID, "R0000001");
        dataset.Add(DicomTag.RequestedProcedureDescription, "骨密度測定");

        var scheduledStep = new DicomDataset
        {
            { DicomTag.Modality, "BMD" },
            { DicomTag.ScheduledStationAETitle, "ASCUBE_MWM" },
            { DicomTag.ScheduledProcedureStepStartDate, DateTime.Today },
            { DicomTag.ScheduledProcedureStepStartTime, DateTime.Today },
            { DicomTag.ScheduledProcedureStepDescription, "骨密度測定" },
        };
        dataset.Add(new DicomSequence(DicomTag.ScheduledProcedureStepSequence, scheduledStep));

        return dataset;
    }
}
