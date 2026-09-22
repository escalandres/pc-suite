using System.Security.Principal;
using Microsoft.Extensions.Logging;
using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.Elevation;

/// <summary>
/// Marco de elevación por operación (Task 005). Orden de defensas:
/// 1. Lista blanca ANTES de nada: ID desconocido = cero lanzamientos y cero
///    prompts UAC.
/// 2. Helper <c>PCSuite.Elevated.exe</c> presente junto a la app.
/// 3. Lanzamiento con <see cref="ProcessRequest.RequiresElevation"/>
///    (ShellExecute "runas"; el prompt lo genera Windows) vía
///    <see cref="IProcessRunner"/>.
/// 4. Único intercambio request/response por pipe nombrado: el hijo valida
///    identidad de cliente + nonce y solo opera lo registrado.
/// 5. El proceso elevado es de vida corta: atiende un mensaje y termina; si
///    el padre muere, el hijo expira por su propio tiempo total.
/// Los errores vuelven como códigos controlados (nunca stack traces); solo
/// la cancelación externa se propaga como excepción.
/// </summary>
public sealed class ElevationManager : IElevationManager
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(90);

    private readonly IProcessRunner _processRunner;
    private readonly string _helperPath;
    private readonly TimeSpan _responseGracePeriod;
    private readonly ILogger<ElevationManager>? _logger;

    public ElevationManager(
        IProcessRunner processRunner,
        string helperPath,
        TimeSpan? responseGracePeriod = null,
        ILogger<ElevationManager>? logger = null)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _helperPath = helperPath ?? throw new ArgumentNullException(nameof(helperPath));
        _responseGracePeriod = responseGracePeriod ?? TimeSpan.FromSeconds(5);
        _logger = logger;
    }

    public Task<bool> IsAdministratorAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(false);
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return Task.FromResult(principal.IsInRole(WindowsBuiltInRole.Administrator));
    }

    public async Task<ElevationResult> RunElevatedAsync(
        ElevatedOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation is null || string.IsNullOrWhiteSpace(operation.OperationId))
        {
            return ElevationResult.Fail(BridgeErrorCodes.MalformedMessage, "La operación elevada no tiene ID.");
        }

        // Autorización primero: fuera de la lista blanca = sin lanzamiento ni UAC.
        if (!ElevatedOperationIds.Allowed.Contains(operation.OperationId))
        {
            return ElevationResult.Fail(
                BridgeErrorCodes.OperationNotFound,
                $"Unknown elevated operation '{operation.OperationId}'.");
        }

        if (!File.Exists(_helperPath))
        {
            return ElevationResult.Fail(
                BridgeErrorCodes.ElevationFailed,
                "No se encontró el proceso elevado de PCSuite. Reinstala la aplicación.");
        }

        var nonce = ElevatedProtocol.NewNonce(); // secreto: jamás en logs
        var pipeName = $"pcsuite-elev-{Guid.NewGuid():N}";

        // Lanzamiento elevado. Un fallo aquí (p. ej. UAC rechazado) se traduce
        // en código controlado y no se intenta ni abrir el pipe.
        Task<ProcessResult> launchTask;
        try
        {
            launchTask = _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = _helperPath,
                    Arguments = [pipeName, nonce],
                    WorkingDirectory = Path.GetDirectoryName(_helperPath),
                    RequiresElevation = true,
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LaunchFailure(exception);
        }

        using var exchangeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var exchangeTask = ElevatedPipeClient.ExchangeAsync(
            pipeName,
            new ElevatedRequest
            {
                Nonce = nonce,
                Operation = operation.OperationId,
                Payload = operation.Payload,
            },
            ConnectTimeout,
            exchangeCts.Token);

        try
        {
            var finished = await Task.WhenAny(exchangeTask, launchTask).ConfigureAwait(false);

            if (finished == launchTask && launchTask.IsFaulted)
            {
                try
                {
                    await launchTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return LaunchFailure(exception);
                }
            }

            if (finished == launchTask)
            {
                // El hijo terminó antes de completar el intercambio. Su
                // respuesta puede estar ya en el buffer del pipe: se le da una
                // holgura breve antes de darlo por perdido (no se esperan los
                // 90 s de conexión).
                var winner = await Task.WhenAny(exchangeTask, Task.Delay(_responseGracePeriod)).ConfigureAwait(false);
                if (winner == exchangeTask)
                {
                    return await MapExchangeAsync(exchangeTask, cancellationToken).ConfigureAwait(false);
                }

                return ElevationResult.Fail(
                    BridgeErrorCodes.ElevationFailed,
                    "El proceso elevado terminó sin responder.");
            }

            return await MapExchangeAsync(exchangeTask, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // El intercambio siempre termina antes de salir: si no completó,
            // se cancela y se espera su cierre (sin tareas huérfanas).
            exchangeCts.Cancel();
            try
            {
                await exchangeTask.ConfigureAwait(false);
            }
            catch
            {
                // Ya mapeado arriba, o cancelado a propósito al salir.
            }
        }
    }

    private async Task<ElevationResult> MapExchangeAsync(
        Task<ElevatedResponse> exchangeTask,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await exchangeTask.ConfigureAwait(false);
            return response.Success
                ? ElevationResult.Ok(response.Result)
                : ElevationResult.Fail(
                    response.Error?.Code ?? BridgeErrorCodes.ElevationFailed,
                    response.Error?.Message ?? "La operación elevada falló.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return ElevationResult.Fail(
                BridgeErrorCodes.ElevationTimeout,
                "El proceso elevado no respondió a tiempo.");
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "El IPC con el proceso elevado falló.");
            return ElevationResult.Fail(
                BridgeErrorCodes.ElevationFailed,
                "El IPC con el proceso elevado falló.");
        }
    }

    private ElevationResult LaunchFailure(Exception exception)
    {
        _logger?.LogError(exception, "No se pudo lanzar el proceso elevado.");

        // El detalle queda en el log; la UI solo ve un mensaje controlado.
        return exception is ElevationDeclinedException declined
            ? ElevationResult.Fail(BridgeErrorCodes.ElevationCancelled, declined.Message)
            : ElevationResult.Fail(BridgeErrorCodes.ElevationFailed, "No se pudo iniciar el proceso elevado.");
    }
}
