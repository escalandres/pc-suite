using PCSuite.Infrastructure.Processes;
using PCSuite.Infrastructure.WinGet;

namespace PCSuite.Application.Tests;

/// <summary>
/// Codificación de la salida (regla del Task 006): winget escribe UTF-8 crudo
/// en la tubería y ProcessRunner debe respetar ProcessRequest.OutputEncoding.
/// Con la decodificación OEM por omisión cada acento se vuelve mojibake y
/// DESPLAZA las columnas de las tablas posicionales que parsea WinGetTableParser.
/// </summary>
public sealed class ProcessRunnerEncodingTests
{
    [Fact]
    public async Task OutputEncoding_Utf8_DecodesAccentsWithoutMojibake()
    {
        var runner = new ProcessRunner();
        var request = MockProcess.Request("utf8") with
        {
            OutputEncoding = WinGetEncoding.Utf8,
            Timeout = TimeSpan.FromSeconds(30),
        };

        var result = await runner.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Versión 1.2.3 — muñeco", result.StandardOutput.Trim());
        Assert.DoesNotContain('\uFFFD', result.StandardOutput); // sin caracteres de reemplazo
    }

    [Fact]
    public void WinGetEncoding_IsUtf8WithoutBom()
    {
        Assert.Equal("utf-8", WinGetEncoding.Utf8.WebName);
        Assert.Empty(WinGetEncoding.Utf8.GetPreamble()); // sin BOM en la tubería
    }
}
