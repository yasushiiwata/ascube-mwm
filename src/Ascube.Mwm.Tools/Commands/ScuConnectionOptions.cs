namespace Ascube.Mwm.Tools.Commands;

/// <summary>scu サブコマンド共通の接続引数。既定値は CLAUDE.md の使用例に合わせる。</summary>
internal sealed class ScuConnectionOptions
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 11112;

    public string CallingAe { get; set; } = "MWM_SCU";

    public string CalledAe { get; set; } = "ASCUBE_MWM";

    /// <summary>共通の接続引数を1つ消費できれば true。呼び出し側の args ループで使う。</summary>
    public bool TryParse(string[] args, ref int i)
    {
        switch (args[i])
        {
            case "--host" when i + 1 < args.Length:
                Host = args[++i];
                return true;
            case "--port" when i + 1 < args.Length:
                Port = int.Parse(args[++i]);
                return true;
            case "--aet" when i + 1 < args.Length:
                CallingAe = args[++i];
                return true;
            case "--aec" when i + 1 < args.Length:
                CalledAe = args[++i];
                return true;
            default:
                return false;
        }
    }
}
