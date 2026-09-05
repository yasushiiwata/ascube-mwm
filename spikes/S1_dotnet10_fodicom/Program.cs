using System.Text;
using FellowOakDicom;
using FellowOakDicom.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// CLAUDE.md: 日本語エンコードのため起動時に必ず呼ぶ。
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 11112;

var services = new ServiceCollection();
services.AddFellowOakDicom();
using var serviceProvider = services.BuildServiceProvider();

var server = serviceProvider.GetRequiredService<IDicomServerFactory>().Create<S1EchoFindService>(port);

Console.WriteLine($"S1: listening on port {port} (net10.0 / fo-dicom 5.2.6). Press Ctrl+C or Enter to stop.");

// タイムアウト付きで待つ（バックグラウンド実行のテストから安全に終了させるため）。
var timeoutMs = args.Length > 1 && int.TryParse(args[1], out var t) ? t : 120_000;
var sw = System.Diagnostics.Stopwatch.StartNew();
while (server.IsListening && sw.ElapsedMilliseconds < timeoutMs)
{
    await Task.Delay(500);
}

Console.WriteLine("S1: stopping.");

internal sealed class S1EchoFindService : DicomService, IDicomServiceProvider, IDicomCEchoProvider, IDicomCFindProvider
{
    public S1EchoFindService(INetworkStream stream, Encoding fallbackEncoding, ILogger logger, DicomServiceDependencies dependencies)
        : base(stream, fallbackEncoding, logger, dependencies)
    {
    }

    public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        foreach (var pc in association.PresentationContexts)
        {
            // T0 の受入条件は「Success が返る」ことのみ。全 abstract syntax を受け入れる。
            pc.AcceptTransferSyntaxes(
                DicomTransferSyntax.ImplicitVRLittleEndian,
                DicomTransferSyntax.ExplicitVRLittleEndian,
                DicomTransferSyntax.ExplicitVRBigEndian);
        }

        return SendAssociationAcceptAsync(association);
    }

    public Task OnReceiveAssociationReleaseRequestAsync() => SendAssociationReleaseResponseAsync();

    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason)
    {
    }

    // CLAUDE.md 絶対規則 #3: 応答送信中の一方的切断は正常系。再スローしない。
    public void OnConnectionClosed(Exception? exception)
    {
    }

    public Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request)
        => Task.FromResult(new DicomCEchoResponse(request, DicomStatus.Success));

    public async IAsyncEnumerable<DicomCFindResponse> OnCFindRequestAsync(DicomCFindRequest request)
    {
        // CLAUDE.md 絶対規則 #2: 該当0件は Success かつ結果なし。ここでは固定1件を返す。
        var response = new DicomCFindResponse(request, DicomStatus.Pending);
        response.Dataset = new DicomDataset
        {
            { DicomTag.PatientID, "0001234" },
            { DicomTag.PatientName, "S1^SPIKE" },
        };
        yield return response;

        yield return new DicomCFindResponse(request, DicomStatus.Success);
        await Task.CompletedTask;
    }
}
