using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Processes;
using PCSuite.Core.Wsl;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.WinGet;

namespace PCSuite.Infrastructure.Wsl;

/// <summary>
/// Implementación de <see cref="IWslService"/> sobre el CLI wsl.exe
/// (Task 007 / §12 / §53):
/// - El frontend solo envía payloads cero: los argv son arreglos fijos de
///   <see cref="WslArguments"/> (sin shell, nunca "wsl …" arbitrario).
/// - TODAS las invocaciones (lecturas y mutaciones) se serializan con un
///   semaphore de 1, también durante una elevación, para que ni --update ni
///   --shutdown corran a la vez sobre el mismo binario.
/// - Las lecturas degradan (fallo → null → unknown / parcial), salvo el
///   timeout, que es un fallo controlado (WSL_TIMEOUT). `wsl --update` puede
///   elevar POR OPERACIÓN vía IElevationManager si el fallo parece de permisos
///   (UAC solo de Windows, nunca simulado); --shutdown/--shutdown jamás elevan.
/// - Los fallos controlados vuelven como WslException (código + mensaje aptos
///   para la UI) o como WslMutationOutcome estructurado; jamás stack traces.
/// </summary>
public sealed class WslService : IWslService
{
    private const string ReadTimeoutMessage = "wsl no respondió a tiempo. Inténtalo de nuevo.";

    private const string MutationTimeoutMessage = "La operación wsl superó el tiempo límite y fue terminada.";

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MutationTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromSeconds(900);

    private readonly ExecutableLocator _locator;
    private readonly IProcessRunner _processRunner;
    private readonly IElevationManager _elevation;
    private readonly ILogger<WslService>? _logger;

    /// <summary>Serie de 1: wsl no soporta ejecuciones solapadas.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WslService(
        ExecutableLocator locator,
        IProcessRunner processRunner,
        IElevationManager elevationManager,
        ILogger<WslService>? logger = null)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _elevation = elevationManager ?? throw new ArgumentNullException(nameof(elevationManager));
        _logger = logger;
    }

    public async Task<WslStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = _locator.Find("wsl");
            if (path is null)
            {
                // Sin wsl.exe no se lanza ningún proceso ni se consulta si se
                // es administrador: estado "desconocido" total.
                return new WslStatus(false, null, WslStates.Unknown, null, null);
            }

            var version = Exit0(await RunAsync(path, WslArguments.Version, ReadTimeout, ReadTimeoutMessage, cancellationToken).ConfigureAwait(false));
            var status = Exit0(await RunAsync(path, WslArguments.Status, ReadTimeout, ReadTimeoutMessage, cancellationToken).ConfigureAwait(false));
            var list = Exit0(await RunAsync(path, WslArguments.List, ReadTimeout, ReadTimeoutMessage, cancellationToken).ConfigureAwait(false));

            var state = WslStates.Unknown;
            if (list is not null)
            {
                var running = WslParser.ParseList(list.StandardOutput)
                    .Any(distro => string.Equals(distro.State, "Running", StringComparison.OrdinalIgnoreCase));
                state = running ? WslStates.Running : WslStates.Stopped;
            }

            return new WslStatus(
                true,
                path,
                state,
                version is null ? null : WslParser.LineValue(version.StandardOutput),
                status is null ? null : WslParser.LineValue(status.StandardOutput));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<WslDistro>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = _locator.Find("wsl");
            if (path is null)
            {
                throw new WslException(BridgeErrorCodes.NotInstalled, "No se encontró wsl.exe en la PATH.");
            }

            var result = await RunAsync(path, WslArguments.List, ReadTimeout, ReadTimeoutMessage, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<WslDistro> distros = result is null ? [] : WslParser.ParseList(result.StandardOutput);
            if (result is { ExitCode: 0 } || distros.Count > 0)
            {
                // Filas interpretables: se devuelven aunque wsl haya quejado
                // (mismo criterio que la consulta winget).
                return distros;
            }

            throw new WslException(BridgeErrorCodes.WslFailed, WslOutput.FailureMessage(result));
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<WslMutationOutcome> UpdateAsync(CancellationToken cancellationToken = default) =>
        MutateAsync(ElevatedOperationIds.WslUpdate, WslArguments.Update, UpdateTimeout, cancellationToken);

    public Task<WslMutationOutcome> ShutdownAsync(CancellationToken cancellationToken = default) =>
        MutateAsync(null, WslArguments.Shutdown, MutationTimeout, cancellationToken);

    public Task<WslMutationOutcome> StartAsync(CancellationToken cancellationToken = default) =>
        MutateAsync(null, WslArguments.Start, MutationTimeout, cancellationToken);

    private async Task<WslMutationOutcome> MutateAsync(
        string? elevatedOperationId,
        string[] args,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = _locator.Find("wsl");
            if (path is null)
            {
                throw new WslException(BridgeErrorCodes.NotInstalled, "No se encontró wsl.exe en la PATH.");
            }

            var result = await RunAsync(path, args, timeout, MutationTimeoutMessage, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                return new WslMutationOutcome(false, -1, false, WslOutput.FailureMessage(null));
            }

            if (result.ExitCode == 0)
            {
                return new WslMutationOutcome(true, 0, false, null);
            }

            if (elevatedOperationId is null || !WslOutput.NeedsElevation(result))
            {
                // Fallo de wsl sin indicios de permisos (p. ej. E_INVALIDARG) o
                // una operación que nunca eleva (--shutdown/--start): resultado
                // estructurado, sin UAC.
                return new WslMutationOutcome(false, result.ExitCode, false, WslOutput.Detail(result));
            }

            if (await _elevation.IsAdministratorAsync().ConfigureAwait(false))
            {
                // Reintentar elevado no aportaría nada: ya corre con privilegios
                // y el fallo NO es de permisos.
                return new WslMutationOutcome(false, result.ExitCode, false, WslOutput.Detail(result));
            }

            _logger?.LogInformation(
                "wsl requirió privilegios (código {ExitCode}); se pide elevación por operación.",
                result.ExitCode);

            // El semaphore sigue tomado durante toda la elevación: ninguna
            // otra operación wsl se lanza mientras el hijo elevado trabaja.
            var elevated = await _elevation.RunElevatedAsync(
                new ElevatedOperation
                {
                    OperationId = elevatedOperationId,
                    Payload = null,
                },
                cancellationToken).ConfigureAwait(false);

            if (!elevated.Success)
            {
                // p. ej. ELEVATION_CANCELLED si el usuario rechaza el UAC.
                throw new WslException(
                    elevated.ErrorCode ?? BridgeErrorCodes.ElevationFailed,
                    elevated.ErrorMessage ?? "No se pudo ejecutar la operación elevada.");
            }

            var dto = elevated.Data is { } element
                ? JsonSerializer.Deserialize<WslMutationResult>(element.GetRawText(), BridgeJson.Options)
                : null;
            if (dto is null)
            {
                throw new WslException(
                    BridgeErrorCodes.ElevationFailed,
                    "El proceso elevado no devolvió un resultado válido.");
            }

            return new WslMutationOutcome(dto.Succeeded, dto.ExitCode, dto.Elevated, dto.Detail);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Ejecuta wsl.exe: timeout → WslException(WSL_TIMEOUT) controlada;
    /// cancelación → se repropaga; cualquier otra excepción del runner
    /// (p. ej. binario ilegible) → log de aviso y null, para que las lecturas
    /// degraden en vez de romper.
    /// </summary>
    private async Task<ProcessResult?> RunAsync(
        string path,
        string[] args,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = path,
                    Arguments = args,
                    Timeout = timeout,
                    OutputEncoding = WslEncoding.Utf16Le,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProcessTimeoutException)
        {
            throw new WslException(BridgeErrorCodes.WslTimeout, timeoutMessage);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "No se pudo ejecutar wsl {Args}.", string.Join(' ', args));
            return null;
        }
    }

    /// <summary>Solo una salida con código 0 cuenta como lectura válida.</summary>
    private static ProcessResult? Exit0(ProcessResult? result) => result is { ExitCode: 0 } ? result : null;
}
