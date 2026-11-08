using System.Threading.Tasks;
using PCSuite.Contracts;
using PCSuite.Infrastructure.Web;

namespace PCSuite.Application.Tests;

/// <summary>Task 040: pruebas del WebInspectorService (métodos puros, sin red).</summary>
public sealed class WebInspectorServiceTests
{
    private readonly WebInspectorService _svc = new();

    [Theory]
    [InlineData("https://www.google.com", "safe")]
    [InlineData("https://exаmple.com", "suspicious")] // homógrafo cirílico
    public async Task InspectUrl_Verdict(string url, string expected)
    {
        var r = await _svc.InspectUrlAsync(url);
        Assert.Equal(expected, r.Verdict);
    }

    [Fact]
    public async Task InspectUrl_Punycode_Flagged()
    {
        var r = await _svc.InspectUrlAsync("https://xn--e1afmkfd.xn--p1ai/");
        Assert.True(r.Punycode);
        Assert.Equal("suspicious", r.Verdict);
    }

    [Fact]
    public async Task InspectUrl_Typosquatting_Detected()
    {
        var r = await _svc.InspectUrlAsync("https://gogle.com");
        Assert.Contains("google", r.TyposquatCandidates);
    }

    [Theory]
    [InlineData("https://example.com/path?utm_source=x&fbclid=y&keep=1",
                "https://example.com/path?keep=1")]
    [InlineData("https://example.com/", "https://example.com/")]
    public async Task CleanTracking_RemovesParams(string input, string expected)
    {
        var r = _svc.CleanTracking(input);
        Assert.Equal(expected, r.CleanedUrl);
    }

    [Fact]
    public async Task CompareUrls_DetectsDifferences()
    {
        var r = _svc.CompareUrls("https://a.com/x?q=1", "https://b.com/y?q=2");
        Assert.True(r.Differences.Count > 1);
    }

    [Fact]
    public async Task CompareUrls_Identical()
    {
        var r = _svc.CompareUrls("https://a.com/x", "https://a.com/x");
        Assert.Contains("idénticas", r.Differences[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckTls_RealHost_NoThrow()
    {
        var r = await _svc.CheckTlsAsync("example.com");
        Assert.NotNull(r);
        Assert.Contains(r.Verdict, new[] { "safe", "suspicious", "invalid" });
    }
}