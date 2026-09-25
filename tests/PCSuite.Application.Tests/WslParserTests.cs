using PCSuite.Core.Wsl;
using PCSuite.Infrastructure.Wsl;

namespace PCSuite.Application.Tests;

/// <summary>
/// WslParser: valor de la primera línea con ':' (versión / distribución por
/// defecto) y filas de `wsl --list --verbose` contra fixtures reales. La basura
/// se descarta en silencio y el parser nunca lanza.
/// </summary>
public sealed class WslParserTests
{
    // ------------------------------------------------------------------
    // LineValue
    // ------------------------------------------------------------------

    [Fact]
    public void LineValue_VersionFixture_ReturnsVersion() =>
        Assert.Equal("2.7.14.0", WslParser.LineValue(WslFixtures.Read("wsl-version.txt")));

    [Fact]
    public void LineValue_StatusFixture_ReturnsDefaultDistro() =>
        Assert.Equal("docker-desktop", WslParser.LineValue(WslFixtures.Read("wsl-status.txt")));

    [Fact]
    public void LineValue_SkipsEmptyLeadingLines_UsesFirstNonEmpty() =>
        Assert.Equal("2.7.14.0", WslParser.LineValue("\r\n\r\nVersión de WSL: 2.7.14.0\r\notra línea"));

    [Fact]
    public void LineValue_WithoutSeparator_ReturnsWholeTrimmedLine() =>
        Assert.Equal("sin separador", WslParser.LineValue("  sin separador  \r\notra"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\r\n  \r\n")]
    public void LineValue_EmptyOutput_ReturnsNull(string? output) =>
        Assert.Null(WslParser.LineValue(output));

    // ------------------------------------------------------------------
    // ParseList
    // ------------------------------------------------------------------

    [Fact]
    public void ParseList_RealFixture_ReturnsSingleDistro()
    {
        var distros = WslParser.ParseList(WslFixtures.Read("wsl-list.txt"));

        Assert.Equal(new WslDistro("docker-desktop", "Stopped", 2), Assert.Single(distros));
    }

    [Fact]
    public void ParseList_ActiveMarkerIsStripped_AndStateKeptRaw()
    {
        var distros = WslParser.ParseList(
            "  NAME              STATE           VERSION\r\n"
            + "* Ubuntu            Running         2\r\n"
            + "  Debian            Stopped         1");

        Assert.Equal(2, distros.Count);
        Assert.Equal(new WslDistro("Ubuntu", "Running", 2), distros[0]);
        Assert.Equal(new WslDistro("Debian", "Stopped", 1), distros[1]);
        Assert.DoesNotContain(distros, distro => distro.Name.StartsWith('*'));
    }

    [Fact]
    public void ParseList_NameWithSingleSpace_IsKeptIntact()
    {
        var distros = WslParser.ParseList("* Debian Buster      Stopped         1");

        Assert.Equal(new WslDistro("Debian Buster", "Stopped", 1), Assert.Single(distros));
    }

    [Fact]
    public void ParseList_HeaderOnly_ReturnsEmpty() =>
        Assert.Empty(WslParser.ParseList("  NAME              STATE           VERSION\r\n"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\r\n")]
    [InlineData("basura sin columnas")]
    [InlineData("  NAME  STATE  VERSION\r\n  sin-número  Running  dos")]
    public void ParseList_GarbageOrEmpty_ReturnsEmpty(string? output) =>
        Assert.Empty(WslParser.ParseList(output));
}
