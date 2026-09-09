namespace Ascube.Mwm.Core.Config;

/// <summary>プロファイルファイルの読込段階（JSON構文・ファイル欠落など）で発生した致命的な問題。</summary>
public sealed class ProfileFormatException(string sourceFile, string message)
    : Exception($"{sourceFile}: {message}")
{
    public string SourceFile { get; } = sourceFile;
}
