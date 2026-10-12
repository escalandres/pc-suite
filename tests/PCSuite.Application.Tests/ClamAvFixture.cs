using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Application.Jobs;
using PCSuite.Core.Events;
using PCSuite.Core.Jobs;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.ClamAV;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.Operations;

namespace PCSuite.Application.Tests;

/// <summary>
/// ClamAvService + fakes para los tests del Task 008: carpeta temporal con (o
/// sin) clamscan.exe y freshclam.exe falsos, runner de procesos en memoria,
/// elevación en memoria, winget en memoria e historial en archivo temporal de
/// esa misma carpeta. installDirectories vacío: los tests jamás tocan los
/// directorios reales del equipo.
/// </summary>
internal sealed class ClamAvFixture : IDisposable
{
    private readonly TempExecutablePath _temp;

    private ClamAvFixture(
        TempExecutablePath temp,
        string? clamScanPath,
        string? freshclamPath,
        FakeProcessRunner runner,
        FakeElevationManager elevation,
        FakeWinGetService winget,
        ClamAvHistoryStore history,
        ClamAvService service,
        IJobManager jobs,
        CommandRouter router)
    {
        _temp = temp;
        ClamScanPath = clamScanPath;
        FreshclamPath = freshclamPath;
        Runner = runner;
        Elevation = elevation;
        Winget = winget;
        History = history;
        Service = service;
        Jobs = jobs;
        Router = router;
    }

    /// <summary>Ruta del clamscan.exe falso, o null si la carpeta no lo tiene.</summary>
    public string? ClamScanPath { get; }

    /// <summary>Ruta del freshclam.exe falso, o null si la carpeta no lo tiene.</summary>
    public string? FreshclamPath { get; }

    public FakeProcessRunner Runner { get; }

    public FakeElevationManager Elevation { get; }

    public FakeWinGetService Winget { get; }

    public ClamAvHistoryStore History { get; }

    public ClamAvService Service { get; }

    /// <summary>JobManager real de los escaneos (para polls de los tests).</summary>
    public IJobManager Jobs { get; }

    /// <summary>Router con las operaciones clamav.* + job.* que registra MainForm.</summary>
    public CommandRouter Router { get; }

    /// <summary>Carpeta temporal (para crear archivos objetivo del customScan).</summary>
    public string TempDirectory => _temp.DirectoryPath;

    public static ClamAvFixture Create(
        FakeProcessRunner runner,
        bool withClamAv = true,
        bool withFreshclam = true)
    {
        var temp = new TempExecutablePath();
        var clamScanPath = withClamAv ? temp.Add("clamscan.exe") : null;
        var freshclamPath = withClamAv && withFreshclam ? temp.Add("freshclam.exe") : null;
        var elevation = new FakeElevationManager();
        var winget = new FakeWinGetService();
        var history = new ClamAvHistoryStore(Path.Combine(temp.DirectoryPath, "scan-history.json"));
        var service = new ClamAvService(
            new ExecutableLocator(temp.DirectoryPath),
            runner,
            elevation,
            winget,
            history,
            installDirectories: Array.Empty<string>());

        // JobManager real con publisher hacia un no-op (los tests leen
        // directamente fx.Jobs; no necesitan el puente).
        var jobs = new JobManager(
            new BridgeJobEventPublisher(NoOpEventPublisher.Instance));

        var router = new CommandRouter(
            new IOperation[]
            {
                new ClamAvStatusOperation(service),
                new ClamAvInstallOperation(service),
                new ClamAvUpdateDefinitionsOperation(service),
                new ClamAvQuickScanOperation(service, jobs),
                new ClamAvFullScanOperation(service, jobs),
                new ClamAvCustomScanOperation(service, jobs),
                new ClamAvHistoryOperation(service),
                new JobGetOperation(jobs),
                new JobCancelOperation(jobs),
            },
            NullLogger<CommandRouter>.Instance);

        return new ClamAvFixture(
            temp, clamScanPath, freshclamPath, runner, elevation, winget, history, service, jobs, router);
    }

    public void Dispose() => _temp.Dispose();

    /// <summary>IEventPublisher que descarta todo: para fixture y asserts rápidos.</summary>
    public sealed class NoOpEventPublisher : IEventPublisher
    {
        public static readonly NoOpEventPublisher Instance = new();

        public void SendEvent<TPayload>(string eventId, TPayload payload)
        {
        }
    }
}
