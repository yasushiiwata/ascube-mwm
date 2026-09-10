using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace Ascube.Mwm.Tools.Commands;

/// <summary>実装指示書 v2 T11：<c>mwm-scu scu echo</c>。</summary>
internal static class ScuEchoCommand
{
    public const string Usage = "使い方: mwm-scu scu echo [--host <h>] [--port <p>] [--aet <calling>] [--aec <called>]";

    public static async Task<int> RunAsync(string[] args)
    {
        var conn = new ScuConnectionOptions();
        for (var i = 0; i < args.Length; i++)
        {
            if (!conn.TryParse(args, ref i))
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }
        }

        var client = DicomClientFactory.Create(conn.Host, conn.Port, false, conn.CallingAe, conn.CalledAe);
        DicomStatus? status = null;
        var request = new DicomCEchoRequest();
        request.OnResponseReceived += (_, response) => status = response.Status;

        try
        {
            await client.AddRequestAsync(request);
            await client.SendAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return 1;
        }

        if (status == DicomStatus.Success)
        {
            Console.WriteLine($"OK: C-ECHO Success（{conn.Host}:{conn.Port}, aet={conn.CallingAe}, aec={conn.CalledAe}）");
            return 0;
        }

        Console.WriteLine($"NG: C-ECHO の応答が Success ではありません: {status}");
        return 1;
    }
}
