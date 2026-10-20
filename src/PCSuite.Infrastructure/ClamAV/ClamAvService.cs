using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCSuite.Contracts;
using PCSuite.Core.ClamAV;
using PCSuite.Core.Events;
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
/// - Los escaneos corren ELEVADOS (decisión del autor 2026-10-18, antes
///   "nunca elevan"): un UAC puntual por escaneo para leer también los
///   archivos del sistema. Como ShellExecute("runas") no redirige stdout, el
///   reporte vive en un log temporal (--log) que el padre taila en vivo:
///   mismo progreso por archivo que con pipes, y el contenido del log se
///   devuelve como StandardOutput para que el parser no cambie.
/// - Los fallos controlados vuelven como outcomes estructurados o como
///   ClamAvException (código + mensaje aptos para la UI), jamás stack traces.
/// </summary>
public sealed class ClamAvService : IClamAvService
{
    /// <summary>Paquete winget exacto: MSI de ámbito máquina que instala en %ProgramFiles%\ClamAV.</summary>
    private const string WingetPackageId = "Cisco.ClamAV";

    /// <summary>Task 021: desenlace de escaneo para el host (toast de amenazas).</summary>
    public event Action<ClamAvScanOutcome, string>? ScanCompleted;

    private const string NotInstalledMessage = "ClamAV no está instalado. Usa clamav.install para instalarlo.";

    private const string ReadTimeoutMessage = "ClamAV no respondió a tiempo. Inténtalo de nuevo.";

    private const string UpdateTimeoutMessage = "La actualización de definiciones superó el tiempo límite y fue terminada.";

    private const string ScanTimeoutMessage = "El escaneo superó el tiempo límite y fue terminado.";

    /// <summary>
/// Mensaje informativo para escaneo parcial (exit code 2 con resumen).
/// No es un error: el escaneo completó pero algunos archivos no fueron accesibles.
/// Desde 2026-10-18 los escaneos corren ELEVADOS: lo que queda sin leer son
/// archivos bloqueados en exclusiva (pagefile, hives en uso), no falta de permisos.
/// </summary>
    private const string PartialScanDetail =
        "Escaneo completado con accesos limitados: algunos archivos no se pudieron leer " +
        "(bloqueados en exclusiva por otro proceso, cifrados o puntos de reanálisis). " +
        "Esto es normal en escaneos de disco. Para mayor cobertura, cierre aplicaciones y reintente.";

    /// <summary>Mensaje cuando el usuario descarta el prompt UAC del escaneo.</summary>
    private const string UacDeclinedMessage =
        "Se rechazó el permiso de administrador (UAC): el escaneo no se ejecutó.";

    /// <summary>
    /// Intervalo de poll del tail del log de escaneo. 400 ms: progreso
    /// perceptible sin bombardear el disco (el log se escribe en ráfagas).
    /// </summary>
    private static readonly TimeSpan ScanLogPollInterval = TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromSeconds(900);

    // 1 h (medición empírica 2026-10-08): el "quick" escanea Escritorio,
    // Descargas y Temp del usuario, que crecen arbitrariamente. Con zips de
    // ~1 GB (ClamAV los descomprime) se midió un ritmo de ~70 líneas/min:
    // ~5.300 archivos ≈ 72 min. Con los 20 min originales el escaneo era
    // imposible de completar y moría en timeout.
    private static readonly TimeSpan QuickScanTimeout = TimeSpan.FromSeconds(3600);

    private static readonly TimeSpan CustomScanTimeout = TimeSpan.FromSeconds(3600);

    private static readonly TimeSpan FullScanTimeout = TimeSpan.FromSeconds(7200);

    /// <summary>
    /// Nombre del archivo de configuración de freshclam.
    /// </summary>
    private const string FreshclamConfigFileName = "freshclam.conf";

    /// <summary>
    /// Directorio por defecto para la base de datos de definiciones (en ProgramData, escribible con elevación).
    /// </summary>
    private static readonly string DefaultDatabaseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ClamAV",
        "database");

    private readonly ExecutableLocator _locator;
    private readonly IProcessRunner _processRunner;
    private readonly IElevationManager _elevation;
    private readonly IWinGetService _winget;
    private readonly ClamAvHistoryStore _history;
    private readonly ILogger<ClamAvService>? _logger;
    private readonly IReadOnlyList<string> _installDirectories;
    private readonly IEventPublisher? _events;

    /// <summary>Serie de 1: escaneos y freshclam no se ejecutan a la vez sobre las mismas definiciones.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ClamAvService(
        ExecutableLocator locator,
        IProcessRunner processRunner,
        IElevationManager elevationManager,
        IWinGetService wingetService,
        ClamAvHistoryStore history,
        ILogger<ClamAvService>? logger = null,
        IReadOnlyList<string>? installDirectories = null,
        IEventPublisher? events = null)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _elevation = elevationManager ?? throw new ArgumentNullException(nameof(elevationManager));
        _winget = wingetService ?? throw new ArgumentNullException(nameof(wingetService));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _logger = logger;
        _installDirectories = installDirectories ?? ClamAvPaths.DefaultInstallDirectories();
        _events = events;
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

    public async Task<ClamAvMutationOutcome> UpdateDefinitionsAsync(
        CancellationToken cancellationToken = default,
        IProgress<ClamAvFreshclamProgress>? progress = null)
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

            var clamAvDirectory = Path.GetDirectoryName(freshclam)!;
            // Asegurar freshclam.conf y directorio de BD antes de ejecutar (best-effort).
            EnsureFreshclamConfig(clamAvDirectory);

            // Determinar directorio de base de datos desde freshclam.conf (o default).
            var databaseDirectory = GetDatabaseDirectory(clamAvDirectory);
            
            // Si el directorio de BD está en ubicación protegida (Program Files, etc.),
            // ejecutar freshclam CON ELEVACIÓN desde el inicio.
            var requiresElevation = IsProtectedDirectory(databaseDirectory);
            
            ProcessResult? result;
            if (requiresElevation)
            {
                _logger?.LogInformation(
                    "Directorio de BD de ClamAV en ubicación protegida ({Dir}); ejecutando freshclam elevado desde el inicio.",
                    databaseDirectory);
                result = await RunFreshclamElevatedAsync(
                    freshclam,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // WorkingDirectory = carpeta de ClamAV: freshclam lee su conf y escribe ahí.
                result = await RunAsyncWithProgress(
                    freshclam,
                    ClamAvArguments.Freshclam,
                    UpdateTimeout,
                    UpdateTimeoutMessage,
                    cancellationToken,
                    Path.GetDirectoryName(freshclam),
                    progress).ConfigureAwait(false);
            }

            if (result is null)
            {
                return new ClamAvMutationOutcome(false, -1, false, ClamAvOutput.FailureMessage(null));
            }

            if (result.ExitCode == 0)
            {
                return new ClamAvMutationOutcome(true, 0, requiresElevation, null);
            }

            // Si la ejecución elevada falló por algo que NO es permisos (red, config),
            // no reintentamos sin elevación (ya estábamos elevados).
            if (requiresElevation)
            {
                var detail = ClamAvOutput.Detail(result) ?? $"freshclam terminó con código {result.ExitCode}.";
                return new ClamAvMutationOutcome(false, result.ExitCode, true, detail);
            }

            // Ejecución sin elevación falló: comprobar si merece reintento elevado.
            var detailNonElevated = ClamAvOutput.Detail(result) ?? "";
            var isLikelyPermissionIssue = ClamAvOutput.NeedsElevation(result)
                || IsGenericFreshclamFailure(detailNonElevated);

            if (!isLikelyPermissionIssue || await _elevation.IsAdministratorAsync().ConfigureAwait(false))
            {
                return new ClamAvMutationOutcome(false, result.ExitCode, false, detailNonElevated);
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

    /// <summary>
    /// Ejecuta freshclam SIN elevación, con streaming de progreso.
    /// </summary>
    private async Task<ProcessResult?> RunFreshclamWithProgressAsync(
        string freshclamPath,
        CancellationToken cancellationToken,
        IProgress<ClamAvFreshclamProgress>? progress)
    {
        var progressReporter = new FreshclamProgressReporter(progress, _events);
        try
        {
            return await _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = freshclamPath,
                    Arguments = ClamAvArguments.Freshclam,
                    Timeout = UpdateTimeout,
                    WorkingDirectory = Path.GetDirectoryName(freshclamPath),
                    OnStandardOutput = line => progressReporter.OnOutputLine(line),
                    OnStandardError = line => progressReporter.OnErrorLine(line),
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProcessTimeoutException)
        {
            throw new ClamAvException(BridgeErrorCodes.ClamAvTimeout, UpdateTimeoutMessage);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "No se pudo ejecutar freshclam.");
            return null;
        }
        finally
        {
            progressReporter.Complete();
        }
    }

    /// <summary>
    /// Ejecuta freshclam CON elevación (ShellExecute "runas"). Sin streaming (limitación de ShellExecute).
    /// </summary>
    private async Task<ProcessResult?> RunFreshclamElevatedAsync(
        string freshclamPath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = freshclamPath,
                    Arguments = ClamAvArguments.Freshclam,
                    Timeout = UpdateTimeout,
                    WorkingDirectory = Path.GetDirectoryName(freshclamPath),
                    RequiresElevation = true, // ShellExecute con "runas"
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProcessTimeoutException)
        {
            throw new ClamAvException(BridgeErrorCodes.ClamAvTimeout, UpdateTimeoutMessage);
        }
        catch (ElevationDeclinedException)
        {
            throw new ClamAvException(BridgeErrorCodes.ElevationCancelled, "El usuario canceló el control de cuentas de usuario.");
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "No se pudo ejecutar freshclam elevado.");
            return null;
        }
    }

    /// <summary>
    /// Obtiene el directorio de base de datos desde freshclam.conf o usa el default.
    /// </summary>
    private static string GetDatabaseDirectory(string clamAvDirectory)
    {
        var configPath = Path.Combine(clamAvDirectory, FreshclamConfigFileName);
        if (!File.Exists(configPath))
        {
            return DefaultDatabaseDirectory;
        }

        try
        {
            var lines = File.ReadAllLines(configPath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("DatabaseDirectory", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var dir = parts[1].Trim().Trim('"', '\'');
                        if (!string.IsNullOrWhiteSpace(dir))
                        {
                            return Path.GetFullPath(dir);
                        }
                    }
                }
            }
        }
        catch
        {
            // Si falla la lectura, usar default.
        }
        return DefaultDatabaseDirectory;
    }

    /// <summary>
    /// Determina si un directorio está en una ubicación protegida que requiere elevación para escribir.
    /// </summary>
    private static bool IsProtectedDirectory(string directory)
    {
        try
        {
            var fullPath = Path.GetFullPath(directory).ToLowerInvariant();
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToLowerInvariant();
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).ToLowerInvariant();
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System).ToLowerInvariant();

            return fullPath.StartsWith(programFiles)
                || fullPath.StartsWith(programFilesX86)
                || fullPath.StartsWith(windows)
                || fullPath.StartsWith(system);
        }
        catch
        {
            return false;
        }
    }

    public Task<ClamAvScanOutcome> QuickScanAsync(
        CancellationToken cancellationToken = default,
        IProgress<ClamAvScanProgress>? progress = null) =>
        ScanAsync(ClamAvScanKinds.Quick, null, ClamAvArguments.QuickScan(), QuickScanTimeout, cancellationToken, progress);

    public Task<ClamAvScanOutcome> FullScanAsync(
        CancellationToken cancellationToken = default,
        IProgress<ClamAvScanProgress>? progress = null)
    {
        // Discos fijos listos en este instante (ponytail: unidades extraíbles
        // y de red quedan fuera del "full"; el customScan cubre cualquier ruta).
        var roots = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
            .Select(drive => drive.Name)
            .ToArray();
        return ScanAsync(ClamAvScanKinds.Full, null, ClamAvArguments.FullScan(roots), FullScanTimeout, cancellationToken, progress);
    }

    public async Task<ClamAvScanOutcome> CustomScanAsync(
        string path,
        CancellationToken cancellationToken = default,
        IProgress<ClamAvScanProgress>? progress = null)
    {
        var fullPath = ValidateScanPath(path);
        return await ScanAsync(
            ClamAvScanKinds.Custom,
            fullPath,
            ClamAvArguments.CustomScan(fullPath),
            CustomScanTimeout,
            cancellationToken,
            progress).ConfigureAwait(false);
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
        CancellationToken cancellationToken,
        IProgress<ClamAvScanProgress>? progress = null)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var clamscan = FindClamScan();
            if (clamscan is null)
            {
                throw new ClamAvException(BridgeErrorCodes.NotInstalled, NotInstalledMessage);
            }

            var result = await RunScanElevatedWithProgressAsync(
                clamscan,
                arguments,
                timeout,
                ScanTimeoutMessage,
                cancellationToken,
                kind,
                progress).ConfigureAwait(false);
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

            var outcome = new ClamAvScanOutcome(
                succeeded,
                result.ExitCode,
                parsed.Threats,
                parsed.ScannedFiles ?? 0,
                parsed.ScannedDirectories ?? 0,
                infected,
                parsed.EngineVersion,
                parsed.KnownViruses,
                detail);

            // Task 021: desenlace estructurado para el host (toast de
            // amenazas). Solo cuando el escaneo llegó a correr — igual
            // que el historial.
            ScanCompleted?.Invoke(outcome, kind);
            return outcome;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Ejecuta clamscan ELEVADO (decisión del autor 2026-10-18: los tres modos
    /// de escaneo piden UAC — con permisos se leen también los archivos del
    /// sistema y desaparecen los "Can't fstat"/"Access denied" de archivos
    /// protegidos). Un solo prompt UAC por escaneo, generado por Windows vía
    /// ShellExecute("runas").
    ///
    /// "runas" no permite redirigir stdout, así que el reporte de clamscan se
    /// dirige a un log temporal (--log) que el padre TAILA en vivo: el
    /// progreso por archivo llega igual que con pipes. Al terminar, el
    /// contenido del log se devuelve COMO StandardOutput: el parser y el
    /// caller no cambian en nada.
    /// </summary>
    private async Task<ProcessResult?> RunScanElevatedWithProgressAsync(
        string clamscanPath,
        string[] arguments,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken,
        string scanKind,
        IProgress<ClamAvScanProgress>? progress)
    {
        var progressReporter = new ClamScanProgressReporter(progress, _events, scanKind);
        var logPath = Path.Combine(
            Path.GetTempPath(),
            $"pcsuite-clamav-scan-{Guid.NewGuid():N}.log");
        try
        {
            var executeTask = _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = clamscanPath,
                    Arguments = ClamAvArguments.WithScanLog(arguments, logPath),
                    Timeout = timeout,
                    RequiresElevation = true,
                },
                cancellationToken);

            // Tail en vivo del log: mientras clamscan corre (y UAC pendiente),
            // cada ciclo entrega solo las líneas NUEVAS al mismo reporter que
            // usaban los pipes — el progreso por archivo es idéntico.
            var offset = 0L;
            var partialLine = new StringBuilder();
            while (!executeTask.IsCompleted)
            {
                await Task.Delay(ScanLogPollInterval, cancellationToken).ConfigureAwait(false);
                foreach (var line in ClamAvScanLogTail.ReadNewLines(logPath, ref offset, partialLine))
                {
                    progressReporter.OnOutputLine(line);
                }
            }

            var result = await executeTask.ConfigureAwait(false);

            // Desagüe final: lo que quedó escrito entre el último ciclo y la
            // salida del proceso.
            foreach (var line in ClamAvScanLogTail.ReadNewLines(logPath, ref offset, partialLine))
            {
                progressReporter.OnOutputLine(line);
            }
            if (partialLine.Length > 0)
            {
                progressReporter.OnOutputLine(partialLine.ToString());
            }

            // El contenido del log ES la salida del escaneo: el caller parsea
            // result.StandardOutput exactamente igual que con pipes.
            var logContent = File.Exists(logPath)
                ? await File.ReadAllTextAsync(logPath, cancellationToken).ConfigureAwait(false)
                : string.Empty;

            return new ProcessResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = logContent,
                StandardError = string.Empty,
            };
        }
        catch (ElevationDeclinedException)
        {
            // El usuario descartó el prompt UAC: resultado controlado, sin
            // drama y sin escaneo a medias.
            throw new ClamAvException(BridgeErrorCodes.ElevationCancelled, UacDeclinedMessage);
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
            _logger?.LogWarning(exception, "No se pudo ejecutar clamscan elevado.");
            return null;
        }
        finally
        {
            progressReporter.Complete();
            try
            {
                if (File.Exists(logPath))
                {
                    File.Delete(logPath);
                }
            }
            catch (IOException)
            {
                // best-effort: el temporal queda en %TEMP% a lo sumo hasta la
                // limpieza del sistema.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Helper para parsear la salida de clamscan y emitir progreso tipado.
    /// Formatos típicos de salida (con --infected --recursive):
    /// - "C:\path\to\file.exe: OK"
    /// - "C:\path\to\infected.exe: Win.Trojan.X FOUND"
    /// - "----------- SCAN SUMMARY -----------"
    /// - "Scanned directories: 42"
    /// - "Scanned files: 1234"
    /// - "Infected files: 2"
    /// </summary>
    private sealed class ClamScanProgressReporter
    {
        private readonly IProgress<ClamAvScanProgress>? _progress;
        private readonly IEventPublisher? _events;
        private readonly string _scanKind;
        private long _scannedFiles = 0;
        private long _scannedDirectories = 0;
        private long _infectedFiles = 0;
        private string? _currentDirectory;
        // Estimación dinámica del total de archivos para calcular porcentaje
        // Estimaciones iniciales basadas en tipo de escaneo:
        // quick: ~5,000 archivos, full: ~500,000, custom: ~50,000
        private long _estimatedTotalFiles;
        private bool _summaryReceived = false;
        // Throttle de eventos (cada Report = 1 mensaje IPC al WebView):
        // escaneos largos emiten decenas de miles de líneas. Las líneas
        // regulares se limitan a ~4 eventos/s; amenazas, resumen, errores
        // y Complete pasan siempre.
        private long _lastReportTick = long.MinValue;
        private const long MinReportIntervalMs = 250;

        public ClamScanProgressReporter(IProgress<ClamAvScanProgress>? progress, IEventPublisher? events, string scanKind)
        {
            _progress = progress;
            _events = events;
            _scanKind = scanKind;
            _estimatedTotalFiles = scanKind switch
            {
                ClamAvScanKinds.Quick => 5000,
                ClamAvScanKinds.Full => 500000,
                ClamAvScanKinds.Custom => 50000,
                _ => 10000
            };
        }

        public void OnOutputLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            // Detectar archivo escaneado: "C:\path\file.exe: OK" o "C:\path\file.exe: Threat FOUND"
            var fileMatch = System.Text.RegularExpressions.Regex.Match(
                line, @"^(.+?):\s+(OK|FOUND|ERROR|Access denied)");
            if (fileMatch.Success)
            {
                var filePath = fileMatch.Groups[1].Value.Trim();
                var status = fileMatch.Groups[2].Value;
                _scannedFiles++;

                // Ajustar estimación dinámica: si superamos la estimación, la aumentamos
                if (_scannedFiles > _estimatedTotalFiles)
                {
                    _estimatedTotalFiles = _scannedFiles + 1000; // crecer en bloques de 1000
                }

                // Actualizar directorio actual si cambió
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && directory != _currentDirectory)
                {
                    _currentDirectory = directory;
                    _scannedDirectories++;
                }

                var isInfected = status.Equals("FOUND", StringComparison.OrdinalIgnoreCase);
                if (isInfected) _infectedFiles++;

                // Contadores ya actualizados: si el throttle corta aquí, el
                // siguiente evento (o el resumen exacto) los recoge.
                if (!isInfected && !ThrottleAllows())
                {
                    return;
                }

                Report(new ClamAvScanProgress
                {
                    ScanKind = _scanKind,
                    CurrentFile = filePath,
                    CurrentDirectory = _currentDirectory,
                    ScannedFiles = _scannedFiles,
                    ScannedDirectories = _scannedDirectories,
                    InfectedFiles = _infectedFiles,
                    Percent = _summaryReceived
                        ? (_estimatedTotalFiles > 0 ? (int)Math.Min(100, _scannedFiles * 100 / _estimatedTotalFiles) : 0)
                        : (_estimatedTotalFiles > 0 ? (int)Math.Min(99, _scannedFiles * 100 / _estimatedTotalFiles) : 0),
                    StatusText = isInfected
                        ? $"⚠ Amenaza: {Path.GetFileName(filePath)}"
                        : $"Escaneando: {Path.GetFileName(filePath)}",
                });
                return;
            }

            // Detectar resumen final
            if (line.Contains("SCAN SUMMARY", StringComparison.OrdinalIgnoreCase))
            {
                _summaryReceived = true;
                return;
            }

            // Detectar contadores del resumen
            var scannedDirsMatch = System.Text.RegularExpressions.Regex.Match(
                line, @"Scanned directories:\s*(\d+)");
            if (scannedDirsMatch.Success)
            {
                _scannedDirectories = long.Parse(scannedDirsMatch.Groups[1].Value);
                Report(new ClamAvScanProgress
                {
                    ScanKind = _scanKind,
                    ScannedDirectories = _scannedDirectories,
                    ScannedFiles = _scannedFiles,
                    InfectedFiles = _infectedFiles,
                    Percent = _estimatedTotalFiles > 0 ? (int)Math.Min(100, _scannedFiles * 100 / _estimatedTotalFiles) : 0,
                    StatusText = "Procesando resumen...",
                });
                return;
            }

            var scannedFilesMatch = System.Text.RegularExpressions.Regex.Match(
                line, @"Scanned files:\s*(\d+)");
            if (scannedFilesMatch.Success)
            {
                // El resumen da el total real de archivos escaneados
                _scannedFiles = long.Parse(scannedFilesMatch.Groups[1].Value);
                _estimatedTotalFiles = _scannedFiles; // total real
                _summaryReceived = true;
                Report(new ClamAvScanProgress
                {
                    ScanKind = _scanKind,
                    ScannedFiles = _scannedFiles,
                    ScannedDirectories = _scannedDirectories,
                    InfectedFiles = _infectedFiles,
                    Percent = 100,
                    StatusText = "Procesando resumen...",
                });
                return;
            }

            var infectedMatch = System.Text.RegularExpressions.Regex.Match(
                line, @"Infected files:\s*(\d+)");
            if (infectedMatch.Success)
            {
                _infectedFiles = long.Parse(infectedMatch.Groups[1].Value);
                Report(new ClamAvScanProgress
                {
                    ScanKind = _scanKind,
                    ScannedFiles = _scannedFiles,
                    ScannedDirectories = _scannedDirectories,
                    InfectedFiles = _infectedFiles,
                    Percent = 100,
                    StatusText = "Procesando resumen...",
                });
                return;
            }

            // Línea informativa genérica (ej. errores de acceso)
            if (line.Contains("Access denied", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                Report(new ClamAvScanProgress
                {
                    ScanKind = _scanKind,
                    CurrentFile = line.Length > 200 ? line[..200] + "…" : line,
                    StatusText = "Error de acceso",
                });
            }
        }

        public void OnErrorLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            Report(new ClamAvScanProgress
            {
                ScanKind = _scanKind,
                CurrentFile = line.Length > 200 ? line[..200] + "…" : line,
                StatusText = "Error",
            });
        }

        public void Complete()
        {
            var percent = _summaryReceived
                ? (_estimatedTotalFiles > 0 ? (int)Math.Min(100, _scannedFiles * 100 / _estimatedTotalFiles) : 100)
                : 100;
            Report(new ClamAvScanProgress
            {
                ScanKind = _scanKind,
                ScannedFiles = _scannedFiles,
                ScannedDirectories = _scannedDirectories,
                InfectedFiles = _infectedFiles,
                IsComplete = true,
                Percent = percent,
                StatusText = "Escaneo finalizado",
            });
        }

        /// <summary>Deja pasar un evento si pasó el intervalo mínimo (~4/s).</summary>
        private bool ThrottleAllows()
        {
            var now = Environment.TickCount64;
            if (now - _lastReportTick < MinReportIntervalMs)
            {
                return false;
            }

            _lastReportTick = now;
            return true;
        }

        private void Report(ClamAvScanProgress p)
        {
            _progress?.Report(p);
            if (_events != null)
            {
                var payload = new ClamAvScanProgressPayload
                {
                    CurrentFile = p.CurrentFile,
                    CurrentDirectory = p.CurrentDirectory,
                    ScannedFiles = p.ScannedFiles,
                    ScannedDirectories = p.ScannedDirectories,
                    InfectedFiles = p.InfectedFiles,
                    StatusText = p.StatusText,
                    IsComplete = p.IsComplete,
                    Succeeded = p.Succeeded,
                    Detail = p.Detail,
                    ScanKind = p.ScanKind,
                    Percent = p.Percent,
                };
                _events.SendEvent(EventIds.ClamAvScanProgress, payload);
            }
        }
    }

    /// <summary>
    /// Única entrada del frontend en un escaneo: debe ser ruta absoluta
    /// existente. GetFullPath normaliza "…\..\…" y, al exigir raíz (C:\… o
    /// \\servidor\…), el resultado no puede empezar por "-" ⇒ sin inyección
    /// de opciones de clamscan; y viaja como UN elemento de argv (sin shell).
    /// </summary>
    public string ValidateScanPath(string? path)
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

    /// <summary>
    /// Asegura que freshclam.conf y el directorio de base de datos existan.
    /// Si freshclam.conf no existe, crea uno mínimo apuntando a un directorio
    /// escribible (ProgramData\ClamAV\database). El directorio se crea si no existe.
    /// </summary>
    private static void EnsureFreshclamConfig(string clamAvDirectory)
    {
        var configPath = Path.Combine(clamAvDirectory, FreshclamConfigFileName);
        if (File.Exists(configPath))
        {
            return; // Ya existe, no tocar.
        }

        try
        {
            // Crear directorio de base de datos si no existe.
            Directory.CreateDirectory(DefaultDatabaseDirectory);

            // Config mínima para freshclam: base de datos en ProgramData (escribible con elevación)
            // y espejo oficial. Sin proxy, sin compresión diferencial (keep it simple).
            var config = $@"# Configuración mínima generada por PCSuite
DatabaseDirectory {DefaultDatabaseDirectory}
DatabaseMirror database.clamav.net
MaxAttempts 3
ConnectTimeout 30
ReceiveTimeout 30
";
            File.WriteAllText(configPath, config);
        }
        catch
        {
            // Best-effort: si no se puede crear, freshclam fallará con su error habitual
            // y se mostrará en la UI. No lanzamos para no bloquear la operación.
        }
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

    /// <summary>
    /// Ejecuta freshclam con callbacks de streaming para reportar progreso en tiempo real.
    /// Parsea la salida de freshclam (líneas como "Downloading main.cvd [====>]  45%") y
    /// emite actualizaciones vía <see cref="IProgress{T}"/>.
    /// </summary>
    private async Task<ProcessResult?> RunAsyncWithProgress(
        string path,
        string[] arguments,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken,
        string? workingDirectory,
        IProgress<ClamAvFreshclamProgress>? progress)
    {
        var progressReporter = new FreshclamProgressReporter(progress, _events);
        try
        {
            return await _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = path,
                    Arguments = arguments,
                    Timeout = timeout,
                    WorkingDirectory = workingDirectory,
                    OnStandardOutput = line => progressReporter.OnOutputLine(line),
                    OnStandardError = line => progressReporter.OnErrorLine(line),
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
        finally
        {
            progressReporter.Complete();
        }
    }

    /// <summary>
    /// Helper para parsear la salida de freshclam y emitir progreso tipado.
    /// Formatos típicos:
    /// - "Downloading main.cvd [===================>                   ]  45%"
    /// - "main.cvd updated (version: 60, sigs: 456789, f-level: 60, builder: neo)"
    /// - "ERROR: Can't connect to database.clamav.net"
    /// - "ClamAV update process completed."
    /// </summary>
    private sealed class FreshclamProgressReporter
    {
        private readonly IProgress<ClamAvFreshclamProgress>? _progress;
        private readonly IEventPublisher? _events;
        private string? _currentFile;

        public FreshclamProgressReporter(IProgress<ClamAvFreshclamProgress>? progress, IEventPublisher? events)
        {
            _progress = progress;
            _events = events;
        }

        public void OnOutputLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            // Detectar archivo actual: "Downloading main.cvd [...]"
            var downloadingMatch = System.Text.RegularExpressions.Regex.Match(
                line, @"^Downloading\s+(\S+)\s+\[.*?\]\s*(\d+)%?");
            if (downloadingMatch.Success)
            {
                _currentFile = downloadingMatch.Groups[1].Value;
                var percent = int.Parse(downloadingMatch.Groups[2].Value);
                Report(new ClamAvFreshclamProgress
                {
                    CurrentFile = _currentFile,
                    Percent = percent,
                    StatusText = $"Descargando {_currentFile}...",
                });
                return;
            }

            // Detectar actualización completada: "main.cvd updated (version: 60, sigs: 456789, ...)"
            var updatedMatch = System.Text.RegularExpressions.Regex.Match(
                line, @"^(\S+)\s+updated\s+\(version:\s*(\d+),\s*sigs:\s*(\d+)");
            if (updatedMatch.Success)
            {
                var file = updatedMatch.Groups[1].Value;
                Report(new ClamAvFreshclamProgress
                {
                    CurrentFile = file,
                    Percent = 100,
                    StatusText = $"{file} actualizado (v{updatedMatch.Groups[2].Value}, {updatedMatch.Groups[3].Value} firmas)",
                });
                _currentFile = null;
                return;
            }

            // Detectar error de conexión
            if (line.Contains("ERROR:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Can't connect", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Failed to", StringComparison.OrdinalIgnoreCase))
            {
                Report(new ClamAvFreshclamProgress
                {
                    StatusText = "Error de conexión o descarga",
                    Detail = line,
                });
                return;
            }

            // Detectar finalización exitosa
            if (line.Contains("ClamAV update process completed", StringComparison.OrdinalIgnoreCase))
            {
                Report(new ClamAvFreshclamProgress
                {
                    IsComplete = true,
                    Succeeded = true,
                    StatusText = "Actualización completada",
                    Detail = "ClamAV update process completed.",
                });
                return;
            }

            // Línea informativa genérica
            if (!string.IsNullOrWhiteSpace(line))
            {
                Report(new ClamAvFreshclamProgress
                {
                    CurrentFile = _currentFile,
                    StatusText = line.Length > 100 ? line[..100] + "…" : line,
                });
            }
        }

        public void OnErrorLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            // freshclam a veces escribe errores en stderr
            Report(new ClamAvFreshclamProgress
            {
                StatusText = "Error",
                Detail = line.Length > 200 ? line[..200] + "…" : line,
            });
        }

        public void Complete()
        {
            // Si no se reportó finalización explícita, emitir una genérica
            Report(new ClamAvFreshclamProgress
            {
                IsComplete = true,
                StatusText = "Proceso finalizado",
            });
        }

        private void Report(ClamAvFreshclamProgress p)
        {
            _progress?.Report(p);
            if (_events != null)
            {
                var payload = new ClamAvFreshclamProgressPayload
                {
                    CurrentFile = p.CurrentFile,
                    Percent = p.Percent,
                    DownloadedBytes = p.DownloadedBytes,
                    TotalBytes = p.TotalBytes,
                    BytesPerSecond = p.BytesPerSecond,
                    StatusText = p.StatusText,
                    IsComplete = p.IsComplete,
                    Succeeded = p.Succeeded,
                    Detail = p.Detail,
                };
                _events.SendEvent(EventIds.ClamAvFreshclamProgress, payload);
            }
        }
    }

    /// <summary>
    /// Determina si un fallo de freshclam con mensaje genérico es probable que sea
    /// por permisos (escribir en Program Files / directorio de BD) y merezca
    /// reintento con elevación. freshclam a veces emite "ERROR: Update failed"
    /// sin más detalles cuando no puede escribir en el directorio de la BD.
    /// </summary>
    private static bool IsGenericFreshclamFailure(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return false;

        var lower = detail.ToLowerInvariant();
        // Mensajes genéricos de freshclam que a menudo son por permisos
        return lower.Contains("error: update failed")
            || lower.Contains("update failed")
            || lower.Contains("failed to update")
            || lower.Contains("falló la actualización")
            || lower.Contains("actualización falló");
    }
}
