namespace Ascube.Mwm.Scp.Capture;

/// <summary>
/// 生バイト記録（実装指示書 v2 T9）の設定。出力先は必ず .gitignore 済みのディレクトリに限る
/// （既定の <c>captures/</c> は .gitignore 済み。規則14）。
/// </summary>
public sealed class RawCaptureOptions
{
    public required string OutputDirectory { get; set; }

    /// <summary>この容量を超えたら次のチャンクを待たずロールオーバーする。</summary>
    public long MaxFileSizeBytes { get; set; } = 200L * 1024 * 1024;

    /// <summary>bounded channel の容量。満杯時は DICOM 処理を止めず記録だけ諦める（DropWrite）。</summary>
    public int ChannelCapacity { get; set; } = 4096;
}
