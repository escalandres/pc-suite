using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.Operations;
using PCSuite.Infrastructure.Wsl;

namespace PCSuite.Application.Tests;

/// <summary>
/// WslService + fakes para los tests del Task 007: carpeta temporal con (o
/// sin) wsl.exe falso para la PATH, runner de procesos en memoria y elevación
/// en memoria. El router se construye con las 5 operaciones wsl exactas que
/// registra MainForm.
/// </summary>
internal sealed class WslFixture : IDisposable
{
    private readonly TempExecutablePath _temp;

    private WslFixture(
        TempExecutablePath temp,
        string? wslPath,
        FakeProcessRunner runner,
        FakeElevationManager elevation)
    {
        _temp = temp;
        WslPath = wslPath;
        Runner = runner;
        Elevation = elevation;
        Service = new WslService(
            new ExecutableLocator(temp.DirectoryPath),
            runner,
            elevation);

        Router = new CommandRouter(
            new IOperation[]
            {
                new WslStatusOperation(Service),
                new WslListOperation(Service),
                new WslUpdateOperation(Service),
                new WslShutdownOperation(Service),
                new WslStartOperation(Service),
            },
            NullLogger<CommandRouter>.Instance);
    }

    /// <summary>Ruta del wsl.exe falso, o null si la carpeta no lo tiene.</summary>
    public string? WslPath { get; }

    public FakeProcessRunner Runner { get; }

    public FakeElevationManager Elevation { get; }

    public WslService Service { get; }

    public CommandRouter Router { get; }

    public static WslFixture Create(FakeProcessRunner runner, bool withWsl = true)
    {
        var temp = new TempExecutablePath();
        var wslPath = withWsl ? temp.Add("wsl.exe") : null;
        return new WslFixture(temp, wslPath, runner, new FakeElevationManager());
    }

    public void Dispose() => _temp.Dispose();
}
