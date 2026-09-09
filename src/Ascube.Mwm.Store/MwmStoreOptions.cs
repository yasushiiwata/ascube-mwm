namespace Ascube.Mwm.Store;

public sealed class MwmStoreOptions
{
    /// <summary>ローカルディスクのみ。ネットワーク共有に置くと破損する（規則5・禁止事項6）。</summary>
    public required string DatabasePath { get; set; }

    public required string DeviceProfileId { get; set; }

    /// <summary>消し忘れの保険。設定してからこの時間が経過すると SCP は0件を返す。</summary>
    public TimeSpan CurrentTtl { get; set; } = TimeSpan.FromMinutes(15);
}
