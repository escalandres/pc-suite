using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Processes;
using PCSuite.Core.WinGet;
using PCSuite.Infrastructure.Developers;

namespace PCSuite.Infrastructure.WinGet;

/// <summary>
/// Implementación de <see cref="IWinGetService"/> sobre el CLI winget.
/// Seguridad y robustez (Task 006 / §10.1 / §11):
/// - La entrada del frontend solo llega como payload tipado; la validación y
///   los argv fijos viven en <see cref="WinGetArguments"/> (sin shell, nunca
///   una línea "winget …" arbitraria).
/// - TODAS las invocaciones (lecturas y mutaciones) se serializan con un
///   semaphore de 1: winget no admite ejecuciones concurrentes (corrompe el
///   índice de fuentes con 0x80071130). El semaphore también se sostiene
///   durante una elevación en curso.
/// - Las mutaciones prueban primero SIN privilegios; solo un fallo que parezca
///   de permisos (y no siendo ya administrador) dispara elevación POR
///   OPERACIÓN vía IElevationManager (allow-list compartida + UAC de Windows,
///   nunca simulado).
/// - Los fallos controlados vuelven como WinGetException (código + mensaje
///   aptos para la UI) o como WinGetMutationOutcome estructurado; jamás stack
///   traces ni salidas crudas completas.
/// </summary>
public sealed class WinGetService : IWinGetService
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan MutationTimeout = TimeSpan.FromSeconds(900);

    private readonly ExecutableLocator _locator;
    private readonly IProcessRunner _processRunner;
    private readonly IElevationManager _elevation;
    private readonly ILogger<WinGetService>? _logger;

    /// <summary>Serie de 1: winget no soporta ejecuciones solapadas.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WinGetService(
        ExecutableLocator locator,
        IProcessRunner processRunner,
        IElevationManager elevationManager,
        ILogger<WinGetService>? logger = null)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _elevation = elevationManager ?? throw new ArgumentNullException(nameof(elevationManager));
        _logger = logger;
    }

    public async Task<WinGetDetection> DetectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = _locator.Find("winget");
            var isAdministrator = await _elevation.IsAdministratorAsync().ConfigureAwait(false);

            if (path is null)
            {
                return new WinGetDetection(false, null, null, isAdministrator);
            }

            string? version = null;
            try
            {
                var result = await RunAsync(path, WinGetArguments.Version(), VersionTimeout, cancellationToken)
                    .ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    version = NormalizeVersion(result.StandardOutput);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // La detección no falla por una versión lenta o un binario
                // roto: winget existe y la UI puede indicarlo.
                _logger?.LogWarning(exception, "No se pudo leer la versión de winget.");
            }

            return new WinGetDetection(true, version, path, isAdministrator);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<WinGetPackage>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        // Validación ANTES del semaphore y de cualquier proceso.
        var args = WinGetArguments.Search(query);
        return QueryAsync(args, isSearch: true, [WinGetExitCodes.NoApplicationsFound], cancellationToken);
    }

    public Task<IReadOnlyList<WinGetPackage>> ListAsync(CancellationToken cancellationToken = default) =>
        QueryAsync(
            WinGetArguments.List(),
            isSearch: false,
            [WinGetExitCodes.NoApplicationsFound],
            cancellationToken);

    public Task<IReadOnlyList<WinGetPackage>> UpdatesAsync(CancellationToken cancellationToken = default) =>
        QueryAsync(
            WinGetArguments.Updates(),
            isSearch: false,
            [WinGetExitCodes.NoApplicationsFound, WinGetExitCodes.NoUpdateAvailable],
            cancellationToken);

    public Task<WinGetMutationOutcome> InstallAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var id = WinGetArguments.ValidatePackageId(packageId);
        return MutateAsync(id, WinGetArguments.Install(id), ElevatedOperationIds.WingetInstall, cancellationToken);
    }

    public Task<WinGetMutationOutcome> UpgradeAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var id = WinGetArguments.ValidatePackageId(packageId);
        return MutateAsync(id, WinGetArguments.Upgrade(id), ElevatedOperationIds.WingetUpgrade, cancellationToken);
    }

    public Task<WinGetMutationOutcome> UpgradeAllAsync(CancellationToken cancellationToken = default) =>
        MutateAsync(null, WinGetArguments.UpgradeAll(), ElevatedOperationIds.WingetUpgradeAll, cancellationToken);

    public Task<WinGetMutationOutcome> UninstallAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var id = WinGetArguments.ValidatePackageId(packageId);
        return MutateAsync(id, WinGetArguments.Uninstall(id), ElevatedOperationIds.WingetUninstall, cancellationToken);
    }

    private async Task<IReadOnlyList<WinGetPackage>> QueryAsync(
        string[] args,
        bool isSearch,
        int[] emptyExitCodes,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = ResolveOrThrow();

            ProcessResult result;
            try
            {
                result = await RunAsync(path, args, QueryTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (ProcessTimeoutException)
            {
                throw new WinGetException(
                    BridgeErrorCodes.WingetTimeout,
                    "winget no respondió a tiempo. Inténtalo de nuevo.");
            }

            var packages = WinGetTableParser.Parse(result.StandardOutput)
                .Select(cells => ToPackage(cells, isSearch))
                .ToList();

            if (packages.Count > 0)
            {
                // Filas interpretables: se devuelven aunque winget haya
                // quejado (p. ej. ruido al refrescar el índice).
                return packages;
            }

            if (result.ExitCode == 0 || Array.IndexOf(emptyExitCodes, result.ExitCode) >= 0)
            {
                return packages;
            }

            throw new WinGetException(BridgeErrorCodes.WingetFailed, WinGetOutput.FailureMessage(result));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WinGetMutationOutcome> MutateAsync(
        string? packageId,
        string[] args,
        string elevatedOperationId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = ResolveOrThrow();

            ProcessResult result;
            try
            {
                result = await RunAsync(path, args, MutationTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (ProcessTimeoutException)
            {
                throw new WinGetException(
                    BridgeErrorCodes.WingetTimeout,
                    "La operación winget superó el tiempo límite (15 min) y fue terminada.");
            }

            if (result.ExitCode == 0)
            {
                return new WinGetMutationOutcome(packageId, true, 0, false, null);
            }

            if (!WinGetOutput.NeedsElevation(result))
            {
                // Fallo de winget sin indicios de permisos (p. ej. paquete inexistente):
                // resultado estructurado, sin UAC.
                return new WinGetMutationOutcome(packageId, false, result.ExitCode, false, WinGetOutput.Detail(result));
            }

            if (await _elevation.IsAdministratorAsync().ConfigureAwait(false))
            {
                // Reintentar elevado no aportaría nada: ya corre con privilegios
                // y el fallo NO es de permisos.
                return new WinGetMutationOutcome(packageId, false, result.ExitCode, false, WinGetOutput.Detail(result));
            }

            _logger?.LogInformation(
                "winget requirió privilegios (código {ExitCode}); se pide elevación por operación.",
                result.ExitCode);

            // El semaphore sigue tomado durante toda la elevación: ninguna
            // otra operación winget se lanza mientras el hijo elevado trabaja.
            var elevated = await _elevation.RunElevatedAsync(
                new ElevatedOperation
                {
                    OperationId = elevatedOperationId,
                    Payload = BridgeJson.ToJson(new WinGetPackagePayload { PackageId = packageId ?? string.Empty }),
                },
                cancellationToken).ConfigureAwait(false);

            if (!elevated.Success)
            {
                // p. ej. ELEVATION_CANCELLED si el usuario rechaza el UAC.
                throw new WinGetException(
                    elevated.ErrorCode ?? BridgeErrorCodes.ElevationFailed,
                    elevated.ErrorMessage ?? "No se pudo ejecutar la operación elevada.");
            }

            var dto = elevated.Data is { } element
                ? JsonSerializer.Deserialize<WinGetMutationResult>(element.GetRawText(), BridgeJson.Options)
                : null;
            if (dto is null)
            {
                throw new WinGetException(
                    BridgeErrorCodes.ElevationFailed,
                    "El proceso elevado no devolvió un resultado válido.");
            }

            return new WinGetMutationOutcome(dto.PackageId, dto.Succeeded, dto.ExitCode, dto.Elevated, dto.Detail);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task<ProcessResult> RunAsync(string path, string[] args, TimeSpan timeout, CancellationToken cancellationToken) =>
        _processRunner.ExecuteAsync(
            new ProcessRequest
            {
                FileName = path,
                Arguments = args,
                Timeout = timeout,
                OutputEncoding = WinGetEncoding.Utf8,
            },
            cancellationToken);

    private string ResolveOrThrow()
    {
        var path = _locator.Find("winget");
        if (path is null)
        {
            throw new WinGetException(
                BridgeErrorCodes.NotInstalled,
                "No se encontró winget en la PATH. Instala \"App Installer\" desde Microsoft Store.");
        }

        return path;
    }

    private static WinGetPackage ToPackage(IReadOnlyList<string> cells, bool isSearch)
    {
        var fourth = cells.Count > 3 && !string.IsNullOrEmpty(cells[3]) ? cells[3] : null;
        return isSearch
            ? new WinGetPackage(cells[1], cells[0], cells[2], Match: fourth)
            : new WinGetPackage(cells[1], cells[0], cells[2], Available: fourth);
    }

    private static string? NormalizeVersion(string? output)
    {
        var version = output?.Trim();
        if (string.IsNullOrEmpty(version))
        {
            return null;
        }

        return version.StartsWith('v') || version.StartsWith('V')
            ? version[1..]
            : version;
    }
}
