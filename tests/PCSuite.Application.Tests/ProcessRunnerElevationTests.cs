using PCSuite.Core.Processes;
using PCSuite.Infrastructure.Processes;

namespace PCSuite.Application.Tests;

/// <summary>
/// Contratos de la rama de elevación de ProcessRunner: se validan ANTES de
/// cualquier lanzamiento (cero UAC en los casos rechazados) y el quoting de
/// ShellExecute sigue CommandLineToArgvW.
/// </summary>
public sealed class ProcessRunnerElevationTests
{
    private static string RootedPath => typeof(ProcessRunner).Assembly.Location;

    [Fact]
    public async Task Elevacion_ConModoInteractivo_LanzaArgumentoSinLanzar()
    {
        var runner = new ProcessRunner();
        var request = new ProcessRequest
        {
            FileName = RootedPath,
            RequiresElevation = true,
            Interactive = true,
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => runner.ExecuteAsync(request, CancellationToken.None));

        Assert.Contains("interactivo", exception.Message);
    }

    [Fact]
    public async Task Elevacion_ConCallbacksDeLinea_LanzaArgumentoSinLanzar()
    {
        var runner = new ProcessRunner();
        var request = new ProcessRequest
        {
            FileName = RootedPath,
            RequiresElevation = true,
            OnStandardOutput = _ => { },
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => runner.ExecuteAsync(request, CancellationToken.None));

        Assert.Contains("callbacks", exception.Message);
    }

    [Fact]
    public async Task Elevacion_ConRutaRelativa_LanzaArgumentoSinLanzar()
    {
        var runner = new ProcessRunner();
        var request = new ProcessRequest
        {
            FileName = "helper.exe",
            RequiresElevation = true,
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => runner.ExecuteAsync(request, CancellationToken.None));

        Assert.Contains("absoluta", exception.Message);
    }

    [Theory]
    [InlineData(new string[0], "")]
    [InlineData(new string[] { "abc" }, "\"abc\"")]
    [InlineData(new string[] { "with space" }, "\"with space\"")]
    [InlineData(new string[] { "", "x" }, "\"\" \"x\"")]
    [InlineData(new string[] { "say \"hi\"" }, "\"say \\\"hi\\\"\"")]
    [InlineData(new string[] { "C:\\dir\\" }, "\"C:\\dir\\\\\"")]
    [InlineData(new string[] { "a\\b", "c" }, "\"a\\b\" \"c\"")]
    public void BuildShellArguments_SigueLasReglasDeCommandLineToArgvW(string[] arguments, string expected)
    {
        Assert.Equal(expected, ProcessRunner.BuildShellArguments(arguments));
    }
}
