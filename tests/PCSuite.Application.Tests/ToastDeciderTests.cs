using PCSuite.Infrastructure.Notifications;

namespace PCSuite.Application.Tests;

/// <summary>
/// Tests del decisor de toasts (Task 021). Lógica PURA: qué evento produce
/// qué notificación y cuándo se suprime. La regla de oro: app con foco
/// NUNCA toastea (los banners internos ya cubren ese caso).
/// </summary>
public sealed class ToastDeciderTests
{
    [Theory]
    [InlineData(ToastKind.IncomingFiles)]
    [InlineData(ToastKind.IncomingMessage)]
    [InlineData(ToastKind.PairRequested)]
    [InlineData(ToastKind.FilesReceived)]
    [InlineData(ToastKind.MessageReceived)]
    [InlineData(ToastKind.ThreatsFound)]
    [InlineData(ToastKind.UpdateAvailable)]
    public void AppFocused_AlwaysSuppresses(ToastKind kind)
    {
        var decision = ToastDecider.Decide(new ToastInput(
            Kind: kind,
            AppFocused: true,
            PeerName: "SKLA-X",
            ItemCount: 3,
            TotalBytes: 12_000_000,
            MessageText: "hola",
            ThreatCount: 2,
            Version: "0.7.0"));

        Assert.False(decision.Show);
        Assert.Null(decision.Title);
        Assert.Null(decision.Body);
    }

    [Fact]
    public void IncomingFiles_TitleCarriesPeerAndCount()
    {
        var decision = ToastDecider.Decide(new ToastInput(
            Kind: ToastKind.IncomingFiles,
            AppFocused: false,
            PeerName: "SKLA-X",
            ItemCount: 3,
            TotalBytes: 12_582_912));

        Assert.True(decision.Show);
        Assert.Equal("SKLA-X quiere enviarte 3 archivo(s)", decision.Title);
        Assert.Contains("12 MB", decision.Body);
        Assert.Contains("acéptalo", decision.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IncomingMessage_DoesNotLeakContent()
    {
        // El contenido NO existe todavía en la solicitud (llega al aprobar):
        // el toast solo anuncia el mensaje, sin adelantar texto.
        var decision = ToastDecider.Decide(new ToastInput(
            Kind: ToastKind.IncomingMessage,
            AppFocused: false,
            PeerName: "SKLA-X"));

        Assert.True(decision.Show);
        Assert.Equal("SKLA-X te envió un mensaje", decision.Title);
        Assert.DoesNotContain("hola", decision.Body ?? string.Empty);
    }

    [Fact]
    public void MessageReceived_PreviewsText_SingleLine_Truncated()
    {
        var longText = "línea con salto\n" + new string('a', 200) + "\nfinal";
        var decision = ToastDecider.Decide(new ToastInput(
            Kind: ToastKind.MessageReceived,
            AppFocused: false,
            PeerName: "SKLA-X",
            MessageText: longText));

        Assert.True(decision.Show);
        Assert.Equal("Mensaje de SKLA-X", decision.Title);
        Assert.DoesNotContain("\n", decision.Body);
        Assert.EndsWith("…", decision.Body);
        Assert.True(decision.Body!.Length <= ToastDecider.MessagePreviewMax + 1);
    }

    [Fact]
    public void PairRequested_RemindsToCompareCodes()
    {
        var decision = ToastDecider.Decide(new ToastInput(
            Kind: ToastKind.PairRequested,
            AppFocused: false,
            PeerName: "SKLA-X"));

        Assert.Equal("SKLA-X quiere emparejarse con este equipo", decision.Title);
        Assert.Contains("código", decision.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FilesReceived_ReportsCountAndVerification()
    {
        var decision = ToastDecider.Decide(new ToastInput(
            Kind: ToastKind.FilesReceived,
            AppFocused: false,
            PeerName: "SKLA-X",
            ItemCount: 5));

        Assert.Equal("5 archivo(s) recibidos", decision.Title);
        Assert.Contains("SHA-256", decision.Body);
    }

    [Fact]
    public void ThreatsFound_ReportsCount()
    {
        var decision = ToastDecider.Decide(new ToastInput(
            Kind: ToastKind.ThreatsFound,
            AppFocused: false,
            ThreatCount: 2));

        Assert.Equal("ClamAV detectó 2 amenaza(s)", decision.Title);
        Assert.Contains("historial", decision.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateAvailable_CarriesVersion()
    {
        var decision = ToastDecider.Decide(new ToastInput(
            Kind: ToastKind.UpdateAvailable,
            AppFocused: false,
            Version: "0.7.0"));

        Assert.Equal("PCSuite 0.7.0 disponible", decision.Title);
        Assert.Contains("Ajustes", decision.Body);
    }

    [Fact]
    public void NullPeer_FallsBackToNeutralName()
    {
        var decision = ToastDecider.Decide(new ToastInput(
            Kind: ToastKind.PairRequested,
            AppFocused: false,
            PeerName: null));

        Assert.StartsWith("Otro equipo", decision.Title);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(12_582_912, "12 MB")]
    [InlineData(5_500_000_000, "5.1 GB")]
    public void FormatBytes_IsHumanReadable(long bytes, string expected)
    {
        Assert.Equal(expected, ToastDecider.FormatBytes(bytes));
    }

    [Fact]
    public void PreviewMessage_CollapsesWhitespace()
    {
        var result = ToastDecider.PreviewMessage("hola   mundo\n\nde nuevo", 120);

        Assert.Equal("hola mundo de nuevo", result);
    }
}
