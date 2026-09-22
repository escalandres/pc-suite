using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PCSuite.Contracts;

/// <summary>
/// Protocolo IPC del canal de elevación: mensajes JSON de UNA sola línea
/// delimitados por '\n', con tope de tamaño y validación de forma en ambos
/// extremos. Los mensajes contienen el nonce: nunca se escriben en log.
/// No hay pipelining — cada extremo emite un único mensaje por conexión.
/// </summary>
public static class ElevatedProtocol
{
    /// <summary>Tope por mensaje (1 MiB). Más allá se rechaza sin parsear.</summary>
    public const int MaxMessageBytes = 1024 * 1024;

    private const int ReadChunkBytes = 4096;

    /// <summary>
    /// Nonce de sesión: 32 bytes aleatorios en base64url (43 chars).
    /// Secreto compartido por línea de comandos; no se registra en log.
    /// </summary>
    public static string NewNonce() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    public static string SerializeRequest(ElevatedRequest request) =>
        JsonSerializer.Serialize(request, BridgeJson.Options);

    public static string SerializeResponse(ElevatedResponse response) =>
        JsonSerializer.Serialize(response, BridgeJson.Options);

    /// <summary>
    /// Valida la forma de un mensaje entrante. JSON inválido o no-objeto →
    /// MALFORMED_MESSAGE; nonce ausente/vacío → UNAUTHORIZED; operación
    /// ausente/vacía → MALFORMED_MESSAGE. El contenido del payload lo
    /// valida la propia operación.
    /// </summary>
    public static bool TryParseRequest(string message, out ElevatedRequest? request, out BridgeError? error)
    {
        request = null;
        error = null;

        ElevatedRequest? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ElevatedRequest>(message, BridgeJson.Options);
        }
        catch (JsonException)
        {
            error = Malformed("El mensaje no es un objeto JSON válido.");
            return false;
        }

        if (parsed is null)
        {
            error = Malformed("El mensaje no puede ser null.");
            return false;
        }

        if (string.IsNullOrEmpty(parsed.Nonce))
        {
            error = new BridgeError
            {
                Code = BridgeErrorCodes.Unauthorized,
                Message = "Falta el token de sesión.",
            };
            return false;
        }

        if (string.IsNullOrEmpty(parsed.Operation))
        {
            error = Malformed("Falta la operación a ejecutar.");
            return false;
        }

        request = parsed;
        return true;
    }

    /// <summary>
    /// Valida la forma de una respuesta entrante. Debe llevar success; si
    /// success es false, además un error con code y message no vacíos.
    /// </summary>
    public static bool TryParseResponse(string message, out ElevatedResponse? response, out BridgeError? error)
    {
        response = null;
        error = null;

        ElevatedResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ElevatedResponse>(message, BridgeJson.Options);
        }
        catch (JsonException)
        {
            error = Malformed("La respuesta no es un objeto JSON válido.");
            return false;
        }

        if (parsed is null)
        {
            error = Malformed("La respuesta no puede ser null.");
            return false;
        }

        if (!parsed.Success
            && (parsed.Error is null
                || string.IsNullOrEmpty(parsed.Error.Code)
                || string.IsNullOrEmpty(parsed.Error.Message)))
        {
            error = Malformed("La respuesta no es válida: se esperaba success o un error con code y message.");
            return false;
        }

        response = parsed;
        return true;
    }

    /// <summary>
    /// Lee un mensaje (hasta '\n'). Devuelve null si el stream se cierra sin
    /// datos. Los bytes posteriores al '\n' no existen por diseño (un solo
    /// mensaje por emisor).
    /// </summary>
    /// <exception cref="InvalidDataException">El mensaje supera <see cref="MaxMessageBytes"/>.</exception>
    public static async Task<string?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[ReadChunkBytes];

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.Length == 0 ? null : Decode(buffer);
            }

            for (var i = 0; i < read; i++)
            {
                if (chunk[i] == (byte)'\n')
                {
                    if (buffer.Length > MaxMessageBytes)
                    {
                        throw Oversized();
                    }

                    return Decode(buffer);
                }

                buffer.WriteByte(chunk[i]);
                if (buffer.Length > MaxMessageBytes)
                {
                    throw Oversized();
                }
            }
        }
    }

    /// <summary>Escribe un mensaje completo terminado en '\n' y lo fulshea.</summary>
    /// <exception cref="InvalidDataException">El mensaje supera <see cref="MaxMessageBytes"/>.</exception>
    public static async Task WriteMessageAsync(Stream stream, string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message + "\n");
        if (bytes.Length > MaxMessageBytes + 1)
        {
            throw Oversized();
        }

        await stream.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Decode(MemoryStream buffer) => Encoding.UTF8.GetString(buffer.ToArray());

    private static InvalidDataException Oversized() =>
        new($"El mensaje supera el límite de {MaxMessageBytes} bytes.");

    private static BridgeError Malformed(string message) =>
        new() { Code = BridgeErrorCodes.MalformedMessage, Message = message };
}
