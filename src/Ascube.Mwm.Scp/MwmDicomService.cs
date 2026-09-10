using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Core.Config;
using Ascube.Mwm.Core.Dataset;
using Ascube.Mwm.Core.Matching;
using Ascube.Mwm.Store.Audit;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Serialization;
using Microsoft.Extensions.Logging;

namespace Ascube.Mwm.Scp;

/// <summary>
/// C-ECHO SCP ＋ アソシエーション制御（実装指示書 v2 T3）＋ C-FIND（T5 実データ照合 ＋ T6 DatasetBuilder）。
/// フロー：ルーティングでプロファイル解決 → 解決結果をログ出力 → Calling AE 照合 → PC ごとに accept/reject。
/// C-FIND は <see cref="MatchEngine"/> で日付範囲・PatientID・PatientName・Modality を判定し、
/// 一致した実データを <see cref="DatasetBuilder"/>（dataset.elements DSL 駆動）で組み立てて返す。
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
    /// T5：日付範囲・PatientID・PatientName・Modality を判定し、一致した実データのみ返す。
    /// T6：一致した1件は <see cref="DatasetBuilder"/>（dataset.elements DSL）で組み立てる。
    /// T8：要求データセット（DICOM JSON Model）・来歴・0件理由を AuditCFind/AuditCFindItem に記録する。
    /// 1人モデルなので0件か1件。該当0件は Success かつ結果なし（規則2）。
    /// </summary>
    public async IAsyncEnumerable<DicomCFindResponse> OnCFindRequestAsync(DicomCFindRequest request)
    {
        var profile = _resolvedProfile
            ?? throw new InvalidOperationException("C-FIND 処理時にプロファイルが未解決です（アソシエーション確立後のはずです）");
        var association = Association;

        var criteria = QueryCriteriaParser.Parse(request.Dataset);
        var stopwatch = Stopwatch.StartNew();
        var auditItems = new List<AuditCFindItemRecord>();
        var itemIndex = 0;
        var resultCount = 0;
        string? explain = null;
        var completedNormally = false;

        try
        {
            await foreach (var item in Routing.Repository.QueryAsync(criteria, limit: 1))
            {
                if (!MatchEngine.EvaluateModality(criteria.Modality, profile.ScheduledStationModality, profile.ModalityMatching))
                {
                    var reason = $"Modality の指名が一致しません（要求: \"{criteria.Modality}\", 保持: \"{profile.ScheduledStationModality}\", mode={profile.ModalityMatching}）";
                    Logger.LogInformation("C-FIND: {Reason}", reason);
                    auditItems.Add(new AuditCFindItemRecord { ItemIndex = itemIndex++, SuppressedReason = reason });
                    explain ??= reason;
                    continue;
                }

                var built = DatasetBuilder.Build(profile, item, request.Dataset);
                if (built.Suppressed)
                {
                    // 規則4：患者に属する値が欠損・不正な行は捏造せず返さない。
                    Logger.LogWarning(
                        "C-FIND: WorkItemId={WorkItemId} は Suppressed: {Reason}",
                        item.WorkItemId, built.SuppressedReason);
                    auditItems.Add(new AuditCFindItemRecord { ItemIndex = itemIndex++, SuppressedReason = built.SuppressedReason });
                    explain ??= built.SuppressedReason;
                    continue;
                }

                auditItems.Add(new AuditCFindItemRecord
                {
                    ItemIndex = itemIndex++,
                    ProvenanceJson = JsonSerializer.Serialize(built.Provenance),
                });
                resultCount++;
                yield return new DicomCFindResponse(request, DicomStatus.Pending) { Dataset = built.Dataset };
            }

            if (resultCount == 0 && explain is null)
            {
                // 該当0件の理由を名指しする（実装指示書 T8 の受入条件1〜5のうち、
                // CurrentEntry 不在・TTL切れ・日付範囲外・PatientID/PatientName不一致は Repository 側が判定する）。
                var explainResult = await Routing.Repository.ExplainAsync(criteria);
                explain = explainResult.Reason;
            }

            yield return new DicomCFindResponse(request, DicomStatus.Success);
            completedNormally = true;
        }
        finally
        {
            stopwatch.Stop();
            var record = new AuditCFindRecord
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                ProfileId = profile.Id,
                CalledAe = association.CalledAE,
                CallingAe = association.CallingAE,
                RequestJson = DicomJson.ConvertDicomToJson(request.Dataset),
                CriteriaJson = JsonSerializer.Serialize(criteria),
                ResultCount = resultCount,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Status = completedNormally ? "Success" : "Aborted",
                Explain = resultCount == 0 ? explain : null,
                PeerAborted = !completedNormally,
                Items = auditItems,
            };

            try
            {
                await Routing.AuditWriter.RecordCFindAsync(record);
            }
            catch (Exception ex)
            {
                // 監査ログの書き込み失敗で C-FIND 応答そのものを失敗させない。
                Logger.LogError(ex, "C-FIND: 監査ログの書き込みに失敗しました");
            }
        }
    }
}
