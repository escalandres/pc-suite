using PCSuite.Infrastructure.ClamAV;

namespace PCSuite.Application.Tests;

/// <summary>Parseo controlado de la salida de clamscan (fixtures sintéticas, sin ClamAV real).</summary>
public sealed class ClamAvParserTests
{
    // ------------------------------------------------------------------
    // clamscan --version
    // ------------------------------------------------------------------

    [Fact]
    public void ParseVersion_SplitsEngineAndDefinitions()
    {
        var (engine, definitions) = ClamAvParser.ParseVersion(ClamAvFixtures.Version);

        Assert.Equal("1.5.4", engine);
        Assert.Equal("7123456", definitions);
    }

    [Fact]
    public void ParseVersion_WithoutDefinitionBuild_ReturnsEngineOnly()
    {
        var (engine, definitions) = ClamAvParser.ParseVersion("ClamAV 1.5.4 Sat Sep 20 08:29:48 2025");

        Assert.Equal("1.5.4", engine);
        Assert.Null(definitions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ParseVersion_MissingOutput_ReturnsWithoutThrowing(string? output)
    {
        var (engine, definitions) = ClamAvParser.ParseVersion(output);

        Assert.Null(engine);
        Assert.Null(definitions);
    }

    [Fact]
    public void ParseVersion_GarbageFirstLine_ComesBackAsVersion()
    {
        var (engine, definitions) = ClamAvParser.ParseVersion("texto raro");

        Assert.Equal("texto raro", engine);
        Assert.Null(definitions);
    }

    // ------------------------------------------------------------------
    // clamscan <objetivos>
    // ------------------------------------------------------------------

    [Fact]
    public void ParseScan_Clean_ExtractsFullSummary_NoThreats()
    {
        var parsed = ClamAvParser.ParseScan(ClamAvFixtures.CleanScan);

        Assert.True(parsed.HasSummary);
        Assert.Empty(parsed.Threats);
        Assert.Equal(7123456, parsed.KnownViruses);
        Assert.Equal("1.5.4", parsed.EngineVersion);
        Assert.Equal(42, parsed.ScannedDirectories);
        Assert.Equal(1234, parsed.ScannedFiles);
        Assert.Equal(0, parsed.InfectedFiles);
    }

    [Fact]
    public void ParseScan_Threats_SplitsAtLastColonSpace_PathMayContainColon()
    {
        var parsed = ClamAvParser.ParseScan(ClamAvFixtures.InfectedScan);

        Assert.Equal(2, parsed.Threats.Count);
        Assert.Equal(@"C:\Users\test\Downloads\evil.com", parsed.Threats[0].File);
        Assert.Equal("Win.Trojan.FakeMS-123", parsed.Threats[0].Threat);
        Assert.Equal(@"C:\carpeta con: dos puntos\eicar.com", parsed.Threats[1].File);
        Assert.Equal("Eicar-Test-Signature", parsed.Threats[1].Threat);
        Assert.Equal(2, parsed.InfectedFiles);
    }

    [Fact]
    public void ParseScan_NoSummary_ReportsEverythingMissing()
    {
        var parsed = ClamAvParser.ParseScan(ClamAvFixtures.ErrorNoSummary);

        Assert.False(parsed.HasSummary);
        Assert.Empty(parsed.Threats);
        Assert.Null(parsed.ScannedFiles);
        Assert.Null(parsed.ScannedDirectories);
        Assert.Null(parsed.InfectedFiles);
        Assert.Null(parsed.KnownViruses);
        Assert.Null(parsed.EngineVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ParseScan_MissingOutput_IsAnEmptyParse(string? output)
    {
        var parsed = ClamAvParser.ParseScan(output);

        Assert.False(parsed.HasSummary);
        Assert.Empty(parsed.Threats);
    }

    [Fact]
    public void ParseScan_CapsThreatListAt100_SummaryStaysTheSourceOfTruth()
    {
        var threatLines = Enumerable.Range(0, 250)
            .Select(index => $@"C:\virus\{index}.com: Win.Test-{index} FOUND");
        var output = string.Join('\n', threatLines)
            + "\n----------- SCAN SUMMARY -----------\nInfected files: 250";

        var parsed = ClamAvParser.ParseScan(output);

        Assert.Equal(100, parsed.Threats.Count); // recorte de la lista…
        Assert.Equal(250, parsed.InfectedFiles); // …pero el resumen manda
        Assert.True(parsed.HasSummary);
    }
}
