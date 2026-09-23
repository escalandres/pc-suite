using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.Operations;
using PCSuite.Infrastructure.WinGet;

namespace PCSuite.Application.Tests;

/// <summary>
/// WinGetService + fakes para los tests del Task 006: carpeta temporal con
/// (o sin) winget.exe falso para la PATH, runner de procesos en memoria y
/// elevación en memoria. El router se construye con las 8 operaciones winget
/// exactas que registra MainForm.
/// </summary>
internal sealed class WinGetFixture : IDisposable
{
    private readonly TempExecutablePath _temp;

    private WinGetFixture(
        TempExecutablePath temp,
        string? wingetPath,
        FakeProcessRunner runner,
        FakeElevationManager elevation)
    {
        _temp = temp;
        WingetPath = wingetPath;
        Runner = runner;
        Elevation = elevation;
        Service = new WinGetService(
            new ExecutableLocator(temp.DirectoryPath),
            runner,
            elevation);

        Router = new CommandRouter(
            new IOperation[]
            {
                new WinGetDetectOperation(Service),
                new WinGetSearchOperation(Service),
                new WinGetListOperation(Service),
                new WinGetUpdatesOperation(Service),
                new WinGetInstallOperation(Service),
                new WinGetUpgradeOperation(Service),
                new WinGetUpgradeAllOperation(Service),
                new WinGetUninstallOperation(Service),
            },
            NullLogger<CommandRouter>.Instance);
    }

    /// <summary>Ruta del winget.exe falso, o null si la carpeta no lo tiene.</summary>
    public string? WingetPath { get; }

    public FakeProcessRunner Runner { get; }

    public FakeElevationManager Elevation { get; }

    public WinGetService Service { get; }

    public CommandRouter Router { get; }

    public static WinGetFixture Create(FakeProcessRunner runner, bool withWinGet = true)
    {
        var temp = new TempExecutablePath();
        var wingetPath = withWinGet ? temp.Add("winget.exe") : null;
        return new WinGetFixture(temp, wingetPath, runner, new FakeElevationManager());
    }

    public void Dispose() => _temp.Dispose();
}
