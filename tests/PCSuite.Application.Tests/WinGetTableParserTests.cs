using PCSuite.Infrastructure.WinGet;

namespace PCSuite.Application.Tests;

/// <summary>
/// Parser posicional de las tablas de winget contra fixtures reales:
/// cabeceras en español, tablas múltiples (upgrade), filas apretadas,
/// resúmenes/notas descartados y cabeceras en inglés (locale-independiente).
/// </summary>
public sealed class WinGetTableParserTests
{
    [Fact]
    public void Parse_NullOrEmpty_ReturnsNoRows()
    {
        Assert.Empty(WinGetTableParser.Parse(null));
        Assert.Empty(WinGetTableParser.Parse(""));
        Assert.Empty(WinGetTableParser.Parse("   \r\n"));
    }

    [Fact]
    public void Parse_NoMatchMessage_HasNoTable()
    {
        // Mensaje sin línea de separación: winget no encontró nada.
        Assert.Empty(WinGetTableParser.Parse(WinGetFixtures.Read("winget-nomatch.txt")));
    }

    [Fact]
    public void Parse_SearchFixture_ReturnsFourRowsWithAccentedHeader()
    {
        var rows = WinGetTableParser.Parse(WinGetFixtures.Read("winget-search.txt"));

        Assert.Equal(4, rows.Count);
        Assert.All(rows, row => Assert.Equal(3, row.Count));
        Assert.Equal<string>(
            new[] { "Microsoft Visual Studio Code", "Microsoft.VisualStudioCode", "1.138.0" },
            rows[0]);
    }

    [Fact]
    public void Parse_SearchFixture_TightRowFitsColumnsExactly()
    {
        var rows = WinGetTableParser.Parse(WinGetFixtures.Read("winget-search.txt"));

        // La fila más apretada (id de 39 caracteres terminando justo donde
        // empieza la columna de versión): el corte posicional no pierde ni
        // gana caracteres.
        var tight = rows[3];
        Assert.Equal("Microsoft Visual Studio Code Insiders CLI", tight[0]);
        Assert.Equal("Microsoft.VisualStudioCode.Insiders.CLI", tight[1]);
        Assert.Equal("1.127.0", tight[2]);
    }

    [Fact]
    public void Parse_ListFixture_ParsesAll97InstalledPackages()
    {
        var rows = WinGetTableParser.Parse(WinGetFixtures.Read("winget-list.txt"));

        Assert.Equal(97, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(4, row.Count);
            Assert.Matches("^[A-Za-z0-9][A-Za-z0-9._+-]*$", row[1]);
        });

        // Celda con espacio interno: el corte posicional la conserva entera.
        Assert.Contains(rows, row => row[1] == "Python.Launcher" && row[2] == "< 3.14.7");
        Assert.Contains(rows, row => row[1] == "Git.Git" && row[2] == "2.55.0.5" && row[3] == "");
        Assert.Contains(rows, row => row[1] == "Google.Chrome.EXE" && row[3] == "154.0.8037.58");
        Assert.Contains(rows, row => row[1] == "MSYS2.MSYS2" && row[2] == "20251213");
    }

    [Fact]
    public void Parse_UpgradeFixture_ParsesBothTables_AndSkipsSummary()
    {
        var rows = WinGetTableParser.Parse(WinGetFixtures.Read("winget-upgrade.txt"));

        // Dos tablas: 5 filas + MSYS2 en la segunda. El resumen
        // "6 actualizaciones disponibles." y la nota se descartan.
        Assert.Equal(6, rows.Count);
        Assert.Contains(rows, row => row[1] == "MSYS2.MSYS2" && row[2] == "20251213" && row[3] == "20260611");
        Assert.Contains(rows, row => row[1] == "EpicGames.EpicOnlineServices" && row[3] == "4.3.1");
        Assert.DoesNotContain(rows, row => row[1].Contains("actualizaciones", StringComparison.Ordinal));

        // Nombres con espacios y paréntesis se mantienen como nombre.
        Assert.Contains(rows, row => row[0] == "Visual Studio Build Tools 2026 (2)");
        Assert.Contains(rows, row => row[0] == "Ollama version 0.34.2");
    }

    [Fact]
    public void Parse_EnglishHeader_Parses_LocaleIndependently()
    {
        const string output =
            "SOME BANNER TEXT\r\n" +
            "Name    Id          Version\r\n" +
            "----------------------------\r\n" +
            "Foo Bar Foo.App     1.2.3\r\n";

        var rows = WinGetTableParser.Parse(output);

        var row = Assert.Single(rows);
        Assert.Equal("Foo Bar", row[0]);
        Assert.Equal("Foo.App", row[1]);
        Assert.Equal("1.2.3", row[2]);
    }
}
