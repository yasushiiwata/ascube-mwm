using System.Text;

namespace Ascube.Mwm.Tools.Commands;

/// <summary>
/// 生バイトを16進で表示する（規則19・T7：「判定は必ず16進で行う。コンソールはCP932なので画面表示で判定しない」）。
/// ASCII可視域だけをテキストとして併記し、それ以外（日本語含む）は '.' にする（コンソール文字化けを判定材料にしない）。
/// </summary>
internal static class HexDump
{
    public static void Print(byte[] bytes, int bytesPerLine = 16)
    {
        for (var offset = 0; offset < bytes.Length; offset += bytesPerLine)
        {
            var count = Math.Min(bytesPerLine, bytes.Length - offset);
            var hex = new StringBuilder();
            var ascii = new StringBuilder();

            for (var i = 0; i < bytesPerLine; i++)
            {
                if (i < count)
                {
                    var b = bytes[offset + i];
                    hex.Append(b.ToString("x2")).Append(' ');
                    ascii.Append(b is >= 0x20 and <= 0x7E ? (char)b : '.');
                }
                else
                {
                    hex.Append("   ");
                }

                if (i % 8 == 7)
                {
                    hex.Append(' ');
                }
            }

            Console.WriteLine($"{offset:x8}  {hex}|{ascii}|");
        }
    }
}
