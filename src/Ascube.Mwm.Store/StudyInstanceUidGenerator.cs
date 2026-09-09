using System.Globalization;
using System.Numerics;

namespace Ascube.Mwm.Store;

/// <summary>
/// StudyInstanceUID の採番（規則1）。"2.25." + UUID の10進表現（DICOM PS3.5 Annex B.2）。
/// ⚠ Implementation Class UID にはこれを使わない（規則1の注記）。
/// </summary>
public static class StudyInstanceUidGenerator
{
    public static string NewUid()
    {
        Span<byte> bytes = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(bytes);

        // Guid の128ビットを符号なし整数として解釈し10進表記する（先頭ゼロは BigInteger が自然に除去する）。
        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
        return "2.25." + value.ToString(CultureInfo.InvariantCulture);
    }
}
