using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using FellowOakDicom.Network.Client.EventArguments;

namespace Ascube.Mwm.Scp.Tests;

/// <summary>
/// T3（C-ECHO SCP ＋ アソシエーション制御）の受入条件。
/// フロー：ルーティングでプロファイル解決 → Calling AE 照合（strict/logOnly/ignore）→ PC ごとに accept/reject。
/// </summary>
public class AssociationControlTests
{
    [Fact]
    public async Task Echo_CalledAeMatchesProfile_ReturnsSuccess()
    {
        using var server = new ScpTestServer(TestProfileFactory.Build(aeTitle: "ASCUBE_MWM"));

        var status = await RunEchoAsync(server.Port, callingAe: "ANY_SCU", calledAe: "ASCUBE_MWM");

        Assert.Equal(DicomStatus.Success, status);
    }

    [Fact]
    public async Task Association_UnknownCalledAe_RejectedWithCalledAeNotRecognized()
    {
        using var server = new ScpTestServer(TestProfileFactory.Build(aeTitle: "ASCUBE_MWM"));

        var client = DicomClientFactory.Create("127.0.0.1", server.Port, false, "ANY_SCU", "WRONG_AE");
        AssociationRejectedEventArgs? rejected = null;
        client.AssociationRejected += (_, e) => rejected = e;
        await client.AddRequestAsync(new DicomCEchoRequest());

        // 規則10：黙って切らず、理由コード付きの A-ASSOCIATE-RJ を返す。
        // fo-dicom の DicomClient は拒否を例外として表面化するが、切断の代わりに正しい reject が
        // 返ってきたこと自体が受入条件なので、例外の Reason/Source を検証する。
        var ex = await Assert.ThrowsAsync<DicomAssociationRejectedException>(() => client.SendAsync());

        Assert.Equal(DicomRejectReason.CalledAENotRecognized, ex.RejectReason);
        Assert.Equal(DicomRejectSource.ServiceUser, ex.RejectSource);
        Assert.NotNull(rejected);
        Assert.Equal(DicomRejectReason.CalledAENotRecognized, rejected!.Reason);
    }

    [Fact]
    public async Task Association_StrictCallingAeMismatch_RejectedWithCallingAeNotRecognized()
    {
        using var server = new ScpTestServer(
            TestProfileFactory.Build(callingAeMatching: "strict", allowedCallingAeTitles: ["ALLOWED_SCU"]));

        var client = DicomClientFactory.Create("127.0.0.1", server.Port, false, "NOT_ALLOWED", "ASCUBE_MWM");
        AssociationRejectedEventArgs? rejected = null;
        client.AssociationRejected += (_, e) => rejected = e;
        await client.AddRequestAsync(new DicomCEchoRequest());

        var ex = await Assert.ThrowsAsync<DicomAssociationRejectedException>(() => client.SendAsync());

        Assert.Equal(DicomRejectReason.CallingAENotRecognized, ex.RejectReason);
        Assert.Equal(DicomRejectSource.ServiceUser, ex.RejectSource);
        Assert.NotNull(rejected);
        Assert.Equal(DicomRejectReason.CallingAENotRecognized, rejected!.Reason);
    }

    [Fact]
    public async Task Association_StrictCallingAeMatch_Accepted()
    {
        using var server = new ScpTestServer(
            TestProfileFactory.Build(callingAeMatching: "strict", allowedCallingAeTitles: ["ALLOWED_SCU"]));

        var status = await RunEchoAsync(server.Port, callingAe: "ALLOWED_SCU", calledAe: "ASCUBE_MWM");

        Assert.Equal(DicomStatus.Success, status);
    }

    [Fact]
    public async Task Association_LogOnlyCallingAeMismatch_StillAccepted()
    {
        using var server = new ScpTestServer(
            TestProfileFactory.Build(callingAeMatching: "logOnly", allowedCallingAeTitles: ["ALLOWED_SCU"]));

        var status = await RunEchoAsync(server.Port, callingAe: "NOT_ALLOWED", calledAe: "ASCUBE_MWM");

        Assert.Equal(DicomStatus.Success, status);
    }

    [Fact]
    public async Task Association_IgnoreMode_CallingAeMismatchStillAccepted()
    {
        using var server = new ScpTestServer(TestProfileFactory.Build(callingAeMatching: "ignore"));

        var status = await RunEchoAsync(server.Port, callingAe: "ANYTHING", calledAe: "ASCUBE_MWM");

        Assert.Equal(DicomStatus.Success, status);
    }

    [Theory]
    [InlineData("1.2.840.10008.1.2")] // Implicit VR Little Endian
    [InlineData("1.2.840.10008.1.2.1")] // Explicit VR Little Endian
    [InlineData("1.2.840.10008.1.2.2")] // Explicit VR Big Endian
    public async Task Association_EachOfTheThreeTransferSyntaxes_Negotiates(string tsUid)
    {
        using var server = new ScpTestServer(TestProfileFactory.Build());

        var client = DicomClientFactory.Create("127.0.0.1", server.Port, false, "ANY_SCU", "ASCUBE_MWM");
        var ts = DicomTransferSyntax.Parse(tsUid);
        var pc = new DicomPresentationContext(101, DicomUID.Verification);
        pc.AddTransferSyntax(ts);
        client.AdditionalPresentationContexts.Add(pc);

        DicomAssociation? accepted = null;
        client.AssociationAccepted += (_, e) => accepted = e.Association;

        await client.AddRequestAsync(new DicomCEchoRequest());
        await client.SendAsync();

        Assert.NotNull(accepted);
        // fo-dicom の DicomClient は PC ID を送信時に振り直すため、ID ではなく交渉結果そのもの
        // （このTSでAcceptされたPCが存在するか）で検証する。
        Assert.Contains(accepted!.PresentationContexts, p =>
            p.AbstractSyntax == DicomUID.Verification
            && p.Result == DicomPresentationContextResult.Accept
            && p.AcceptedTransferSyntax == ts);
    }

    [Fact]
    public async Task Association_UnsupportedAbstractSyntax_RejectedButAssociationStillAccepted()
    {
        // 規則9：未サポートの PC だけを reject し、アソシエーション自体は成立させる。
        using var server = new ScpTestServer(TestProfileFactory.Build());

        var client = DicomClientFactory.Create("127.0.0.1", server.Port, false, "ANY_SCU", "ASCUBE_MWM");

        var verificationPc = new DicomPresentationContext(1, DicomUID.Verification);
        verificationPc.AddTransferSyntax(DicomTransferSyntax.ExplicitVRLittleEndian);
        client.AdditionalPresentationContexts.Add(verificationPc);

        var unsupportedPc = new DicomPresentationContext(3, DicomUID.SecondaryCaptureImageStorage);
        unsupportedPc.AddTransferSyntax(DicomTransferSyntax.ExplicitVRLittleEndian);
        client.AdditionalPresentationContexts.Add(unsupportedPc);

        DicomAssociation? accepted = null;
        client.AssociationAccepted += (_, e) => accepted = e.Association;

        await client.AddRequestAsync(new DicomCEchoRequest());
        await client.SendAsync();

        Assert.NotNull(accepted);
        var verificationResult = accepted!.PresentationContexts.First(p => p.AbstractSyntax == DicomUID.Verification);
        var unsupportedResult = accepted.PresentationContexts.First(p => p.AbstractSyntax == DicomUID.SecondaryCaptureImageStorage);

        Assert.Equal(DicomPresentationContextResult.Accept, verificationResult.Result);
        Assert.Equal(DicomPresentationContextResult.RejectAbstractSyntaxNotSupported, unsupportedResult.Result);
    }

    private static async Task<DicomStatus?> RunEchoAsync(int port, string callingAe, string calledAe)
    {
        var client = DicomClientFactory.Create("127.0.0.1", port, false, callingAe, calledAe);
        DicomStatus? status = null;
        var request = new DicomCEchoRequest();
        request.OnResponseReceived += (_, response) => status = response.Status;

        await client.AddRequestAsync(request);
        await client.SendAsync();

        return status;
    }
}
