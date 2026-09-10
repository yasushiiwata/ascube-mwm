using System.Linq;
using System.Text;
using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Core.Config;
using Ascube.Mwm.Core.Matching;
using FellowOakDicom;
using FellowOakDicom.Network;
using Microsoft.Extensions.Logging;

namespace Ascube.Mwm.Scp;

/// <summary>
/// C-ECHO SCP ＋ アソシエーション制御（実装指示書 v2 T3）＋ C-FIND（T4 最小実装 → T5 で実データ照合）。
/// フロー：ルーティングでプロファイル解決 → 解決結果をログ出力 → Calling AE 照合 → PC ごとに accept/reject。
/// C-FIND は <see cref="MatchEngine"/> で日付範囲・PatientID・PatientName・Modality を判定し、
/// 一致した実データのみ返す。データセットの組み立ては暫定の直接マッピング（本格的な DSL 駆動は T6 DatasetBuilder）。
/// </summary>
public sealed class MwmDicomService : DicomService, IDicomServiceProvider, IDicomCEchoProvider, IDicomCFindProvider
{
    private static readonly DicomUID[] SupportedAbstractSyntaxes =
    [
        DicomUID.Verification,
        DicomUID.ModalityWorklistInformationModelFind,
    ];

    private DeviceProfile? _resolvedProfile;

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
        _resolvedProfile = profile;

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
    /// T5：日付範囲・PatientID・PatientName・Modality を判定し、一致した実データのみ返す
    /// （実装指示書 v2 T5）。1人モデルなので0件か1件。該当0件は Success かつ結果なし（規則2）。
    /// データセットの組み立ては暫定の直接マッピング（DSL 駆動の一般化は T6 DatasetBuilder）。
    /// </summary>
    public async IAsyncEnumerable<DicomCFindResponse> OnCFindRequestAsync(DicomCFindRequest request)
    {
        var profile = _resolvedProfile
            ?? throw new InvalidOperationException("C-FIND 処理時にプロファイルが未解決です（アソシエーション確立後のはずです）");

        var criteria = QueryCriteriaParser.Parse(request.Dataset);

        await foreach (var item in Routing.Repository.QueryAsync(criteria, limit: 1))
        {
            if (!MatchEngine.EvaluateModality(criteria.Modality, profile.ScheduledStationModality, profile.ModalityMatching))
            {
                Logger.LogInformation(
                    "C-FIND: Modality 不一致のため0件（要求=\"{Requested}\", 保持=\"{Candidate}\", mode={Mode}）",
                    criteria.Modality, profile.ScheduledStationModality, profile.ModalityMatching);
                continue;
            }

            var dataset = TryBuildDataset(item, profile);
            if (dataset is null)
            {
                // 規則4：患者に属する値が欠損している行は捏造せず返さない。
                // 監査への記録（AuditCFindItem 等）は T8 で構造化する。
                Logger.LogWarning(
                    "C-FIND: WorkItemId={WorkItemId} は PatientName が欠損しているため Suppressed（規則4）",
                    item.WorkItemId);
                continue;
            }

            yield return new DicomCFindResponse(request, DicomStatus.Pending) { Dataset = dataset };
        }

        yield return new DicomCFindResponse(request, DicomStatus.Success);
    }

    private static DicomDataset? TryBuildDataset(WorkItemView item, DeviceProfile profile)
    {
        var patientName = ComposePatientName(item);
        if (patientName is null)
        {
            return null;
        }

        var dataset = new DicomDataset();

        // 規則6：SpecificCharacterSet は DicomDataset に最初に設定する。
        dataset.Add(DicomTag.SpecificCharacterSet, profile.SpecificCharacterSet);

        dataset.Add(DicomTag.PatientName, patientName);
        dataset.Add(DicomTag.PatientID, item.StablePatientId);
        dataset.Add(DicomTag.PatientBirthDate, item.BirthDate ?? string.Empty);
        dataset.Add(DicomTag.PatientSex, ToDicomSex(item.Sex));
        dataset.Add(DicomTag.StudyInstanceUID, item.StudyInstanceUid);

        if (item.AccessionNumber is { Length: > 0 } accessionNumber)
        {
            dataset.Add(DicomTag.AccessionNumber, accessionNumber);
        }

        if (item.RequestedProcedureId is { Length: > 0 } requestedProcedureId)
        {
            dataset.Add(DicomTag.RequestedProcedureID, requestedProcedureId);
        }

        if (item.RequestedProcedureDesc is { Length: > 0 } requestedProcedureDesc)
        {
            dataset.Add(DicomTag.RequestedProcedureDescription, requestedProcedureDesc);
        }

        // 規則17：当日測定していない身長・体重はタグごと省略する（前回値を今日の値として送らない）。
        if (item.PatientSizeM is { } sizeM)
        {
            dataset.Add(DicomTag.PatientSize, sizeM);
        }

        if (item.PatientWeightKg is { } weightKg)
        {
            dataset.Add(DicomTag.PatientWeight, weightKg);
        }

        var scheduledStep = new DicomDataset
        {
            { DicomTag.Modality, profile.ScheduledStationModality ?? string.Empty },
            { DicomTag.ScheduledStationAETitle, profile.AeTitle },
            { DicomTag.ScheduledProcedureStepStartDate, item.ScheduledDate },
        };

        if (item.RequestedProcedureDesc is { Length: > 0 } spsDesc)
        {
            scheduledStep.Add(DicomTag.ScheduledProcedureStepDescription, spsDesc);
        }

        dataset.Add(new DicomSequence(DicomTag.ScheduledProcedureStepSequence, scheduledStep));

        return dataset;
    }

    private static string? ComposePatientName(WorkItemView item)
    {
        if (!string.IsNullOrEmpty(item.FamilyNameKanji) || !string.IsNullOrEmpty(item.GivenNameKanji))
        {
            return $"{item.FamilyNameKanji}^{item.GivenNameKanji}";
        }

        if (!string.IsNullOrEmpty(item.FamilyNameKana) || !string.IsNullOrEmpty(item.GivenNameKana))
        {
            return $"{item.FamilyNameKana}^{item.GivenNameKana}";
        }

        return null;
    }

    private static string ToDicomSex(Sex sex) => sex switch
    {
        Sex.Male => "M",
        Sex.Female => "F",
        Sex.Other => "O",
        _ => string.Empty,
    };
}
