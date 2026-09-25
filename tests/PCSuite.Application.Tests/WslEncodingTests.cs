using PCSuite.Infrastructure.Processes;
using PCSuite.Infrastructure.Wsl;

namespace PCSuite.Application.Tests;

/// <summary>
/// Codificación de la salida (regla del Task 007): wsl.exe escribe UTF-16LE
/// crudo en la tubería y ProcessRunner debe respetar ProcessRequest.OutputEncoding
/// (CodePage 1200, sin BOM). Con la decodificación OEM por omisión cada acento
/// se vuelve mojibake.
/// </summary>
public sealed class WslEncodingTests
{
    [Fact]
    public async Task OutputEncoding_Utf16Le_DecodesAccentsWithoutMojibake()
    {
        var runner = new ProcessRunner();
        var request = MockProcess.Request("utf16") with
        {
            OutputEncoding = WslEncoding.Utf16Le,
            Timeout = TimeSpan.FromSeconds(30),
        };

        var result = await runner.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Versión 2.7.14 — muñeco", result.StandardOutput.Trim());
        Assert.DoesNotContain('\uFFFD', result.StandardOutput); // sin caracteres de reemplazo
    }

    [Fact]
    public void WslEncoding_IsUtf16LeWithoutBom()
    {
        Assert.Equal(1200, WslEncoding.Utf16Le.CodePage);
        Assert.Empty(WslEncoding.Utf16Le.GetPreamble()); // sin BOM en la tubería
    }
}
