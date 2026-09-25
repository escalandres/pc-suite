using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCSuite.Contracts;
using PCSuite.Core.ClamAV;
using PCSuite.Core.Elevation;
using PCSuite.Core.Processes;
using PCSuite.Core.WinGet;
using PCSuite.Infrastructure.Developers;

namespace PCSuite.Infrastructure.ClamAV;

/// <summary>
/// Implementación de <see cref="IClamAvService"/> sobre los CLI de ClamAV
/// (Task 008 / §13 / §10.1). PCSuite orquesta, no es el motor antivirus:
/// - La entrada del frontend solo llega como payload tipado; la validación de
///   la ruta y los argv fijos viven aquí y en ClamAvArguments (sin shell,
///   nunca una línea "clamscan …" arbitraria).
/// - Escaneos y freshclam se serializan con un semaphore de 1; status/history
///   quedan FUERA de la puerta para seguir respondiendo durante un escaneo
///   largo de horas.
/// - La instalación delega en winget (paquete fijo Cisco.ClamAV) y reutiliza
///   la elevación por operación del Task 006; freshclam reintenta elevado SOLO
///   si falla por permisos y no es ya administrador (allow-list + UAC generado
///   por Windows, nunca simulado).
/// - Los escaneos NUNCA elevan (ponytail): clamscan lee lo que pueda y los
///   archivos protegidos aparecen como errores parciales en el resumen.
/// - Los fallos controlados vuelven como outcomes estructurados o como
///   ClamAvException (código + mensaje aptos para la UI), jamás stack traces.
/// </summary>
public sealed class ClamAvService : IClamAvService
{
    /// <summary>Paquete winget exacto: MSI de ámbito máquina que instala en %ProgramFiles%\ClamAV.</summary>
    private const string WingetPackageId = "Cisco.ClamAV";

    private const string NotInstalledMessage = "ClamAV no está instalado. Usa clamav.install para instalarlo.";

    private const string ReadTimeoutMessage = "ClamAV no respondió a tiempo. Inténtalo de nuevo.";

    private const string UpdateTimeoutMessage = "La actualización de definiciones superó el tiempo límite y fue terminada.";

    private const string ScanTimeoutMessage = "El escaneo superó el tiempo límite y fue terminado.";

    private const string PartialScanDetail = "Escaneo parcial: alguno de los archivos o carpetas no se pudo leer.";

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromSeconds(900);

    private static readonly TimeSpan QuickScanTimeout = TimeSpan.FromSeconds(900);

    private static readonly TimeSpan CustomScanTimeout = TimeSpan.FromSeconds(3600);

    private static readonly TimeSpan FullScanTimeout = TimeSpan.FromSeconds(7200);

    private readonly ExecutableLocator _locator;
    private readonly IProcessRunner _processRunner;
    private readonly IElevationManager _elevation;
    private readonly IWinGetService _winget;
    private readonly ClamAvHistoryStore _history;
    private readonly ILogger<ClamAvService>? _logger;
    private readonly IReadOnlyList<string> _installDirectories;

    /// <summary>Serie de 1: escaneos y freshclam no se ejecutan a la vez sobre las mismas definiciones.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ClamAvService(
        ExecutableLocator locator,
        IProcessRunner processRunner,
        IElevationManager elevationManager,
        IWinGetService wingetService,
        ClamAvHistoryStore history,
        ILogger<ClamAvService>? logger = null,
        IReadOnlyList<string>? installDirectories = null)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _elevation = elevationManager ?? throw new ArgumentNullException(nameof(elevationManager));
        _winget = wingetService ?? throw new ArgumentNullException(nameof(wingetService));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _logger = logger;
        _installDirectories = installDirectories ?? ClamAvPaths.DefaultInstallDirectories();
    }

    public async Task<ClamAvStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        // Fuera del semaphore: clamscan --version no toca las definiciones y
        // la UI no debe quedar colgada durante un escaneo de horas.
        var clamscan = FindClamScan();
        if (clamscan is null)
        {
            return new ClamAvStatus(false, null, null, null, false);
        }

        var version = await RunAsync(
            clamscan,
            ClamAvArguments.Version,
            ReadTimeout,
            ReadTimeoutMessage,
            cancellationToken).ConfigureAwait(false);
        var parsed = version is { ExitCode: 0 }
            ? ClamAvParser.ParseVersion(version.StandardOutput)
            : default;

        return new ClamAvStatus(
            true,
            clamscan,
            parsed.Engine,
            parsed.Definitions,
            ClamAvPaths.FindFreshclam(clamscan) is not null);
    }

    public async Task<ClamAvMutationOutcome> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (FindClamScan() is not null)
        {
            return new ClamAvMutationOutcome(true, 0, false, "ClamAV ya está instalado.");
        }

        try
        {
            // Id fijo de paquete: si el MSI necesita privilegios, la elevación
            // la resuelve WinGetService con su allow-list del Task 006 (aquí
            // jamás se pide UAC directamente).
            var outcome = await _winget.InstallAsync(WingetPackageId, cancellationToken).ConfigureAwait(false);
            return new ClamAvMutationOutcome(outcome.Succeeded, outcome.ExitCode, outcome.Elevated, outcome.Detail);
        }
        catch (WinGetException exception)
        {
            // Códigos controlados de winget (NOT_INSTALLED, ELEVATION_CANCELLED…)
            // reutilizados tal cual en clamav.install.
            throw new ClamAvException(exception.ErrorCode, exception.ErrorMessage);
        }
    }

    public async Task<ClamAvMutationOutcome> UpdateDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var clamscan = FindClamScan();
            if (clamscan is null)
            {
                throw new ClamAvException(BridgeErrorCodes.NotInstalled, NotInstalledMessage);
            }

            var freshclam = ClamAvPaths.FindFreshclam(clamscan);
            if (freshclam is null)
            {
                throw new ClamAvException(
                    BridgeErrorCodes.NotInstalled,
                    "No se encontró freshclam.exe junto a clamscan.exe.");
            }

            // WorkingDirectory = carpeta de ClamAV: freshclam lee su conf y
            // escribe las definiciones ahí (en Program Files hace falta admin).
            var result = await RunAsync(
                freshclam,
                ClamAvArguments.Freshclam,
                UpdateTimeout,
                UpdateTimeoutMessage,
                cancellationToken,
                Path.GetDirectoryName(freshclam)).ConfigureAwait(false);
            if (result is null)
            {
                return new ClamAvMutationOutcome(false, -1, false, ClamAvOutput.FailureMessage(null));
            }

            if (result.ExitCode == 0)
            {
                return new ClamAvMutationOutcome(true, 0, false, null);
            }

            if (!ClamAvOutput.NeedsElevation(result) || await _elevation.IsAdministratorAsync().ConfigureAwait(false))
            {
                // Sin indicios de permisos (p. ej. freshclam.conf ausente) o ya
                // siendo administrador: reintentar elevado no aportaría nada.
                return new ClamAvMutationOutcome(false, result.ExitCode, false, ClamAvOutput.Detail(result));
            }

            _logger?.LogInformation(
                "freshclam requirió privilegios (código {ExitCode}); se pide elevación por operación.",
                result.ExitCode);

            // El semaphore sigue tomado durante toda la elevación: ninguna
            // otra operación ClamAV se lanza mientras el hijo elevado trabaja.
            var elevated = await _elevation.RunElevatedAsync(
                new ElevatedOperation
                {
                    OperationId = ElevatedOperationIds.ClamavUpdateDefinitions,
                    Payload = null,
                },
                cancellationToken).ConfigureAwait(false);
            if (!elevated.Success)
            {
                throw new ClamAvException(
                    elevated.ErrorCode ?? BridgeErrorCodes.ElevationFailed,
                    elevated.ErrorMessage ?? "No se pudo ejecutar la operación elevada.");
            }

            var dto = elevated.Data is { } element
                ? JsonSerializer.Deserialize<ClamAvMutationResult>(element.GetRawText(), BridgeJson.Options)
                : null;
            if (dto is null)
            {
                throw new ClamAvException(
                    BridgeErrorCodes.ElevationFailed,
                    "El proceso elevado no devolvió un resultado válido.");
            }

            return new ClamAvMutationOutcome(dto.Succeeded, dto.ExitCode, dto.Elevated, dto.Detail);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<ClamAvScanOutcome> QuickScanAsync(CancellationToken cancellationToken = default) =>
        ScanAsync(ClamAvScanKinds.Quick, null, ClamAvArguments.QuickScan(), QuickScanTimeout, cancellationToken);

    public Task<ClamAvScanOutcome> FullScanAsync(CancellationToken cancellationToken = default)
    {
        // Discos fijos listos en este instante (ponytail: unidades extraíbles
        // y de red quedan fuera del "full"; el customScan cubre cualquier ruta).
        var roots = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
            .Select(drive => drive.Name)
            .ToArray();
        return ScanAsync(ClamAvScanKinds.Full, null, ClamAvArguments.FullScan(roots), FullScanTimeout, cancellationToken);
    }

    public async Task<ClamAvScanOutcome> CustomScanAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = ValidateScanPath(path);
        return await ScanAsync(
            ClamAvScanKinds.Custom,
            fullPath,
            ClamAvArguments.CustomScan(fullPath),
            CustomScanTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ClamAvScanHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_history.Read());
    }

    private async Task<ClamAvScanOutcome> ScanAsync(
        string kind,
        string? scanPath,
        string[] arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var clamscan = FindClamScan();
            if (clamscan is null)
            {
                throw new ClamAvException(BridgeErrorCodes.NotInstalled, NotInstalledMessage);
            }

            var result = await RunAsync(clamscan, arguments, timeout, ScanTimeoutMessage, cancellationToken)
                .ConfigureAwait(false);
            if (result is null)
            {
                return new ClamAvScanOutcome(
                    false, -1, [], 0, 0, 0, null, null, ClamAvOutput.FailureMessage(null));
            }

            var parsed = ClamAvParser.ParseScan(result.StandardOutput);
            // 0: limpio; 1: amenazas (un hallazgo es un escaneo VÁLIDO, no un
            // fallo); 2 con resumen: parcial (p. ej. archivos sin permisos en
            // un escaneo completo) y se devuelve como resultado con detalle;
            // sin resumen o >2: fallo estructurado.
            var succeeded = result.ExitCode is 0 or 1
                || (result.ExitCode == 2 && parsed.HasSummary);
            var detail = !succeeded
                ? ClamAvOutput.FailureMessage(result)
                : result.ExitCode > 1 ? PartialScanDetail : null;
            var infected = parsed.InfectedFiles ?? parsed.Threats.Count;

            // Excepciones (no instalado / timeout) NO registran entrada: solo
            // los resultados estructurados de un escaneo que llegó a correr.
            _history.Add(new ClamAvScanHistoryEntry(
                DateTimeOffset.UtcNow,
                kind,
                scanPath,
                succeeded,
                parsed.ScannedFiles ?? 0,
                infected,
                parsed.Threats,
                detail));

            return new ClamAvScanOutcome(
                succeeded,
                result.ExitCode,
                parsed.Threats,
                parsed.ScannedFiles ?? 0,
                parsed.ScannedDirectories ?? 0,
                infected,
                parsed.EngineVersion,
                parsed.KnownViruses,
                detail);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Única entrada del frontend en un escaneo: debe ser ruta absoluta
    /// existente. GetFullPath normaliza "…\..\…" y, al exigir raíz (C:\… o
    /// \\servidor\…), el resultado no puede empezar por "-" ⇒ sin inyección
    /// de opciones de clamscan; y viaja como UN elemento de argv (sin shell).
    /// </summary>
    private static string ValidateScanPath(string? path)
    {
        const string errorCode = BridgeErrorCodes.ClamAvFailed;

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ClamAvException(errorCode, "Indica una ruta absoluta para escanear.");
        }

        if (path.Contains('\0'))
        {
            throw new ClamAvException(errorCode, "La ruta no es válida.");
        }

        if (!Path.IsPathRooted(path))
        {
            throw new ClamAvException(errorCode, "La ruta debe ser absoluta (p. ej. C:\\Descargas).");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ClamAvException(errorCode, "La ruta no es válida.");
        }

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new ClamAvException(errorCode, "La ruta indicada no existe.");
        }

        return fullPath;
    }

    private string? FindClamScan() => ClamAvPaths.FindClamScan(_locator, _installDirectories);

    private async Task<ProcessResult?> RunAsync(
        string path,
        string[] arguments,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        try
        {
            return await _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    // OutputEncoding sin fijar: los binarios CRT de ClamAV
                    // escriben en la codepage OEM de la consola por omisión.
                    FileName = path,
                    Arguments = arguments,
                    Timeout = timeout,
                    WorkingDirectory = workingDirectory,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProcessTimeoutException)
        {
            throw new ClamAvException(BridgeErrorCodes.ClamAvTimeout, timeoutMessage);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "No se pudo ejecutar ClamAV {Arguments}.", string.Join(' ', arguments));
            return null;
        }
    }
}
