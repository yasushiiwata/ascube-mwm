namespace Ascube.Mwm.Abstractions;

public interface IWorklistWriter
{
    /// <summary>今この端末のワークリストに載せる受診者を1人だけ設定する（既存は置き換わる）。</summary>
    Task SetCurrentAsync(WorklistEntry entry, CancellationToken ct = default);

    /// <summary>ワークリストを空にする。以後の C-FIND は0件 + Success を返す。</summary>
    Task ClearCurrentAsync(CancellationToken ct = default);

    /// <summary>現在載っている受診者（無ければ null）。</summary>
    Task<WorklistEntry?> GetCurrentAsync(CancellationToken ct = default);
}
