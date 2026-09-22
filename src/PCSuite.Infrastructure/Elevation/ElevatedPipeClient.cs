using System.IO.Pipes;
using PCSuite.Contracts;

namespace PCSuite.Infrastructure.Elevation;

/// <summary>
/// Lado cliente (proceso NO elevado) del IPC de elevación: conecta al pipe del
/// hijo, envía UN único request y devuelve la respuesta parseada. Sin proceso
/// propio: lo usan <c>ElevationManager</c> y los tests, que así ejercitan el
/// intercambio completo sin pasar por UAC.
/// </summary>
public static class ElevatedPipeClient
{
    /// <exception cref="TimeoutException">El pipe del hijo no apareció a tiempo.</exception>
    public static async Task<ElevatedResponse> ExchangeAsync(
        string pipeName,
        ElevatedRequest request,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken)
    {
        using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        // ConnectAsync(TimeSpan, CancellationToken): el timeout lo aplica el
        // propio pipe (TimeoutException si expira) y el CancellationToken
        // corta la espera si el dueño se cancela.
        await client
            .ConnectAsync(connectTimeout, cancellationToken)
            .ConfigureAwait(false);

        await ElevatedProtocol.WriteMessageAsync(
            client,
            ElevatedProtocol.SerializeRequest(request),
            cancellationToken).ConfigureAwait(false);

        string? incoming;
        try
        {
            incoming = await ElevatedProtocol.ReadMessageAsync(client, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return ElevatedResponse.Fail(BridgeErrorCodes.MalformedMessage, "La respuesta supera el tamaño permitido.");
        }

        if (incoming is null)
        {
            return ElevatedResponse.Fail(
                BridgeErrorCodes.ElevationFailed,
                "El proceso elevado cerró la conexión sin responder.");
        }

        if (!ElevatedProtocol.TryParseResponse(incoming, out var response, out var error))
        {
            return ElevatedResponse.Fail(
                error?.Code ?? BridgeErrorCodes.MalformedMessage,
                error?.Message ?? "La respuesta del proceso elevado no es válida.");
        }

        return response!;
    }
}
