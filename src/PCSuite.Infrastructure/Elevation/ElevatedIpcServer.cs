using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Elevation;

/// <summary>
/// Lado servidor (proceso elevado) del IPC de elevación: acepta UNA conexión,
/// valida identidad de cliente y nonce, ejecuta una operación registrada y
/// devuelve UNA respuesta. El pipe se crea con ACL restringida al SID del
/// usuario actual, con una sola instancia y I/O asíncrono; el nonce se compara
/// en tiempo constante. Ningún mensaje lleva a log (todos contienen el nonce).
/// </summary>
public sealed class ElevatedIpcServer
{
    private static readonly TimeSpan DefaultTotalTimeout = TimeSpan.FromSeconds(60);

    private readonly ElevatedOperationRegistry _registry;
    private readonly TimeSpan _totalTimeout;

    public ElevatedIpcServer(ElevatedOperationRegistry registry, TimeSpan? totalTimeout = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _totalTimeout = totalTimeout ?? DefaultTotalTimeout;
    }

    /// <summary>
    /// Sirve un único request y devuelve la respuesta (también cuando la
    /// validación falla: se intenta enviar al cliente para que vea el error).
    /// La cancelación externa se propaga; la expiración del tiempo total del
    /// servidor se traduce en ELEVATION_TIMEOUT sin lanzar.
    /// </summary>
    public async Task<ElevatedResponse> ServeAsync(
        string pipeName,
        string expectedNonce,
        string expectedClientUserName,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("El IPC de elevación solo existe en Windows.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_totalTimeout);

        NamedPipeServerStream server;
        try
        {
            server = CreateServerStream(pipeName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nombre ocupado o DACL rechazada: mensaje controlado, sin detalles
            // internos que filtrar hacia el padre.
            _ = exception;
            return ElevatedResponse.Fail(BridgeErrorCodes.ElevationFailed, "No se pudo crear el canal de elevación.");
        }

        using (server)
        {
            // 1) Conexión del cliente (dentro del tiempo total del servidor).
            try
            {
                await server.WaitForConnectionAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return ElevatedResponse.Fail(
                    BridgeErrorCodes.ElevationTimeout,
                    "El proceso principal no conectó al canal a tiempo.");
            }

            // 2) Mensaje de request (con tope de tamaño).
            string? requestMessage;
            try
            {
                requestMessage = await ElevatedProtocol.ReadMessageAsync(server, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                // El cliente sigue escribiendo lo que sobra del mensaje: se
                // descarta hasta el fin de trama para poder responder con calma
                // antes de cerrar (si no, el cliente ve "pipe broken" al recibir
                // el rechazo).
                await DrainUntilFrameEndAsync(server, timeoutCts.Token).ConfigureAwait(false);
                return await RespondAsync(
                    server,
                    ElevatedResponse.Fail(BridgeErrorCodes.MalformedMessage, "El mensaje supera el tamaño permitido."),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return ElevatedResponse.Fail(
                    BridgeErrorCodes.ElevationTimeout,
                    "El proceso principal no envió la solicitud a tiempo.");
            }

            if (requestMessage is null)
            {
                return ElevatedResponse.Fail(
                    BridgeErrorCodes.ElevationFailed,
                    "El cliente cerró la conexión sin enviar la solicitud.");
            }

            // 3) Identidad: solo el usuario actual (el padre no elevado).
            //    Se comprueba DESPUÉS de leer: GetImpersonationUserName solo
            //    devuelve el usuario del cliente una vez éste ha escrito
            //    (restricción de la API de Windows) — antes daría falso negativo.
            if (!IsExpectedClient(server, expectedClientUserName))
            {
                return await RespondAsync(
                    server,
                    ElevatedResponse.Fail(BridgeErrorCodes.Unauthorized, "Identidad de cliente no válida."),
                    cancellationToken).ConfigureAwait(false);
            }

            // 4) Forma del request.
            if (!ElevatedProtocol.TryParseRequest(requestMessage, out var request, out var parseError))
            {
                return await RespondAsync(
                    server,
                    ElevatedResponse.Fail(
                        parseError?.Code ?? BridgeErrorCodes.MalformedMessage,
                        parseError?.Message ?? "El mensaje no es válido."),
                    cancellationToken).ConfigureAwait(false);
            }

            // 5) Nonce en tiempo constante: sin token válido no hay ejecución.
            if (!NonceEquals(request!.Nonce, expectedNonce))
            {
                return await RespondAsync(
                    server,
                    ElevatedResponse.Fail(BridgeErrorCodes.Unauthorized, "Token de sesión no válido."),
                    cancellationToken).ConfigureAwait(false);
            }

            // 6) Operación: solo registradas (y por tanto en la lista blanca).
            var operation = _registry.Get(request.Operation);
            if (operation is null)
            {
                return await RespondAsync(
                    server,
                    ElevatedResponse.Fail(
                        BridgeErrorCodes.OperationNotFound,
                        $"Unknown elevated operation '{request.Operation}'."),
                    cancellationToken).ConfigureAwait(false);
            }

            // 7) Ejecución: ninguna excepción escapa hacia el padre.
            OperationResult operationResult;
            try
            {
                operationResult = await operation
                    .ExecuteAsync(new OperationContext { Payload = request.Payload }, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return await RespondAsync(
                    server,
                    ElevatedResponse.Fail(BridgeErrorCodes.ElevationTimeout, "La operación elevada superó el tiempo límite."),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // El hijo no tiene logger: ni stack traces ni mensajes crudos.
                return await RespondAsync(
                    server,
                    ElevatedResponse.Fail(BridgeErrorCodes.OperationFailed, "La operación elevada falló."),
                    cancellationToken).ConfigureAwait(false);
            }

            var response = operationResult.Success
                ? ElevatedResponse.Ok(operationResult.Data)
                : ElevatedResponse.Fail(
                    operationResult.ErrorCode ?? BridgeErrorCodes.OperationFailed,
                    operationResult.ErrorMessage ?? "The operation failed.");

            return await RespondAsync(server, response, cancellationToken).ConfigureAwait(false);
        }
    }

    private static NamedPipeServerStream CreateServerStream(string pipeName)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("El IPC de elevación solo existe en Windows.");
        }

        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException("No se pudo identificar el usuario actual."),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1, // maxNumberOfServerInstances: una sola conexión por hijo
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            4096, // inBufferSize
            4096, // outBufferSize
            security,
            HandleInheritability.None,
            // Importante: additionalAccessRights se ORa directamente en el
            // dwOpenMode de CreateNamedPipe (solo admite bits PIPE_ACCESS_* /
            // FILE_FLAG_*); meter PipeAccessRights.ReadWrite aquí provoca
            // ERROR_INVALID_PARAMETER. Los derechos van en pipeSecurity.
            default);
    }

    private static bool IsExpectedClient(NamedPipeServerStream server, string expectedClientUserName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var clientUserName = server.GetImpersonationUserName();
            if (string.IsNullOrWhiteSpace(clientUserName))
            {
                return false;
            }

            // GetImpersonationUserName suele devolver DOMINIO\usuario y
            // Environment.UserName solo el usuario: se compara por el
            // componente de usuario (la restricción real es el SID de la DACL).
            return string.Equals(
                BareUser(clientUserName),
                BareUser(expectedClientUserName),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string BareUser(string userName)
    {
        var separator = userName.LastIndexOf('\\');
        return separator >= 0 ? userName[(separator + 1)..] : userName;
    }

    /// <summary>
    /// Descarta los bytes restantes de un mensaje que superó el tope hasta
    /// encontrar el '\n' de fin de trama (o un techo duro de 4 MiB). Es
    /// best-effort: ni la cancelación ni un cliente desaparecido abortan el
    /// rechazo MALFORMED ya decidido.
    /// </summary>
    private static async Task DrainUntilFrameEndAsync(Stream server, CancellationToken cancellationToken)
    {
        const int MaxDrainBytes = 4 * 1024 * 1024;
        var buffer = new byte[4096];
        var drained = 0;

        try
        {
            while (drained < MaxDrainBytes)
            {
                var read = await server.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    return;
                }

                if (Array.IndexOf(buffer, (byte)'\n', 0, read) >= 0)
                {
                    return;
                }

                drained += read;
            }
        }
        catch (OperationCanceledException)
        {
            // Best-effort: el rechazo ya está decidido.
        }
        catch (IOException)
        {
            // Cliente ya desaparecido: no queda nada que drenar.
        }
    }

    /// <summary>Comparación en tiempo constante del nonce (UTF-8; longitudes distintas = inválido).</summary>
    private static bool NonceEquals(string? clientNonce, string expectedNonce)
    {
        var clientBytes = Encoding.UTF8.GetBytes(clientNonce ?? string.Empty);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedNonce);

        return clientBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(clientBytes, expectedBytes);
    }

    /// <summary>
    /// Intenta escribir la respuesta al cliente. Un cliente ya desaparecido no
    /// cambia el resultado: el hijo termina justo después.
    /// </summary>
    private static async Task<ElevatedResponse> RespondAsync(
        NamedPipeServerStream server,
        ElevatedResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            await ElevatedProtocol.WriteMessageAsync(
                server,
                ElevatedProtocol.SerializeResponse(response),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // El cliente ya no está ahí (o se canceló): el resultado se conserva.
        }

        return response;
    }
}
