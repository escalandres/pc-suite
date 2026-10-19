using System.Text;
using PCSuite.Infrastructure.ClamAV;

namespace PCSuite.Application.Tests;

/// <summary>
/// Tests del tail incremental del log de escaneo (2026-10-18: los escaneos
/// corren elevados con "runas" — sin pipes, el progreso vive en el --log que
/// el padre poll-ea). Cubre: lectura incremental por offset, línea parcial
/// conservada hasta su salto, \r\n, archivo aún no creado (UAC pendiente) y
/// el tope defensivo de línea sin salto.
/// </summary>
public sealed class ClamAvScanLogTailTests : IDisposable
{
    private readonly TempExecutablePath _temp = new();

    public void Dispose() => _temp.Dispose();

    private string LogPath => Path.Combine(_temp.DirectoryPath, "scan.log");

    [Fact]
    public void ReadNewLines_FileMissing_ReturnsEmpty_WithoutThrowing()
    {
        var offset = 0L;
        var partialLine = new StringBuilder();

        var lines = ClamAvScanLogTail.ReadNewLines(LogPath, ref offset, partialLine);

        Assert.Empty(lines);
        Assert.Equal(0, offset);
    }

    [Fact]
    public void ReadNewLines_IncrementalAppends_OnlyNewLinesEachCycle()
    {
        File.WriteAllText(LogPath, "línea 1\nlínea 2\n");
        var offset = 0L;
        var partialLine = new StringBuilder();

        var first = ClamAvScanLogTail.ReadNewLines(LogPath, ref offset, partialLine);
        Assert.Equal(["línea 1", "línea 2"], first);

        // Nada nuevo: ciclo vacío.
        Assert.Empty(ClamAvScanLogTail.ReadNewLines(LogPath, ref offset, partialLine));

        // clamscan añade más: solo lo nuevo.
        File.AppendAllText(LogPath, "línea 3\n");
        var second = ClamAvScanLogTail.ReadNewLines(LogPath, ref offset, partialLine);
        Assert.Equal(["línea 3"], second);
    }

    [Fact]
    public void ReadNewLines_PartialLineSurvivesUntilNewline()
    {
        File.WriteAllText(LogPath, "línea completa\nlínea a me");
        var offset = 0L;
        var partialLine = new StringBuilder();

        var first = ClamAvScanLogTail.ReadNewLines(LogPath, ref offset, partialLine);
        Assert.Equal(["línea completa"], first);
        Assert.Equal("línea a me", partialLine.ToString());

        // El proceso completa la línea: sale entera, sin duplicar el trozo.
        File.AppendAllText(LogPath, "dias\n");
        var second = ClamAvScanLogTail.ReadNewLines(LogPath, ref offset, partialLine);
        Assert.Equal(["línea a medias"], second);
        Assert.Equal(0, partialLine.Length);
    }

    [Fact]
    public void ReadNewLines_CRLFIsHandled()
    {
        File.WriteAllText(LogPath, "una\r\ndos\r\n");
        var offset = 0L;
        var partialLine = new StringBuilder();

        var lines = ClamAvScanLogTail.ReadNewLines(LogPath, ref offset, partialLine);

        Assert.Equal(["una", "dos"], lines);
    }

    [Fact]
    public void ReadNewLines_NoNewlineAtEof_StaysInPartial()
    {
        File.WriteAllText(LogPath, "resumen sin salto final");
        var offset = 0L;
        var partialLine = new StringBuilder();

        Assert.Empty(ClamAvScanLogTail.ReadNewLines(LogPath, ref offset, partialLine));
        Assert.Equal("resumen sin salto final", partialLine.ToString());
    }

    [Fact]
    public void ReadNewLines_HugePartialLineIsFlushedAsOneLine()
    {
        // Tope defensivo: un megabyte sin '\n' no puede crecer sin control.
        File.WriteAllText(LogPath, new string('x', 2 * 1024 * 1024));
        var offset = 0L;
        var partialLine = new StringBuilder();

        var lines = ClamAvScanLogTail.ReadNewLines(LogPath, ref offset, partialLine);

        var line = Assert.Single(lines);
        Assert.True(line.Length >= 1024 * 1024);
        Assert.Equal(0, partialLine.Length);
    }
}
