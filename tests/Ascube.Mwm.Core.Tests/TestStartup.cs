using System.Runtime.CompilerServices;
using System.Text;

namespace Ascube.Mwm.Core.Tests;

/// <summary>本番の Program.cs と同じく、CP932（半角カナ等）を使うテストのために一度だけ呼ぶ。</summary>
internal static class TestStartup
{
    [ModuleInitializer]
    public static void Init() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}
