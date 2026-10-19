using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace PCSuite.Infrastructure.LocalShare;

/// <summary>
/// Protocolo binario de Local Share v1. Frames de longitud prefijada:
/// [4 bytes magic "PCSH"] [1 byte version] [1 byte msgType] [4 bytes len] [payload].
/// Control = JSON. Datos de archivo = bytes crudos en chunks de 64KB.
/// Documentado en docs/local-share/protocol.md.
/// </summary>
public static class ShareProtocol
{
    public const uint Magic = 0x50435348; // "PCSH"
    /// <summary>
    /// v2 (16.7.3): el flujo por archivo pasa a ser petición-respuesta —
    /// tras FILE_START el receptor responde RESUME con el offset que ya
    /// tiene de intentos anteriores. Un peer v1 moriría esperando datos
    /// tras FILE_START, de ahí el salto de versión.
    /// </summary>
    public const byte Version = 2;
    public const int HeaderSize = 10; // 4 magic + 1 version + 1 type + 4 length
    public const int ChunkSize = 64 * 1024; // 64 KB

    // ------------------------------------------------------------------
    // Message types (v1)
    // ------------------------------------------------------------------

    public const byte MsgHello = 0x01;
    public const byte MsgHelloAck = 0x02;
    public const byte MsgTransferRequest = 0x03;
    public const byte MsgTransferAccept = 0x04;
    public const byte MsgTransferReject = 0x05;
    public const byte MsgFileStart = 0x06;
    public const byte MsgFileData = 0x07;
    public const byte MsgFileEnd = 0x08;
    public const byte MsgTransferComplete = 0x09;
    public const byte MsgTransferCancel = 0x0A;
    public const byte MsgError = 0x0B;

    /// <summary>
    /// Confirmación de fin-a-fin (16.7.2): el receptor la envía tras publicar
    /// TODOS los archivos con éxito. Hasta recibirla, el "Completed" del
    /// emisor no es verdad: el receptor puede fallar al verificar hash o
    /// al publicar (disco lleno, colisión, etc.).
    /// </summary>
    public const byte MsgTransferCompleteAck = 0x0C;

    /// <summary>
    /// Respuesta del receptor a FILE_START (16.7.3): offset en bytes que ya
    /// posee de un intento anterior (0 = desde cero). El emisor busca ese
    /// offset y envía solo el resto; Publish verifica el SHA-256 del archivo
    /// COMPLETO, así que un archivo fuente cambiado entre intentos se
    /// detecta y descarta.
    /// </summary>
    public const byte MsgResume = 0x0D;

    /// <summary>
    /// Emparejamiento (comparación numérica): el iniciador lo envía tras
    /// HELLO_ACK en lugar de TRANSFER_REQUEST. El receptor muestra el código
    /// derivado de ambas huellas y responde ACCEPT o REJECT.
    /// </summary>
    public const byte MsgPairRequest = 0x0E;

    /// <summary>El receptor aceptó el emparejamiento.</summary>
    public const byte MsgPairAccept = 0x0F;

    /// <summary>El receptor rechazó el emparejamiento (o expiró la solicitud).</summary>
    public const byte MsgPairReject = 0x10;

    // ------------------------------------------------------------------
    // Payloads de control (JSON)
    // ------------------------------------------------------------------

    public sealed record HelloPayload(string DeviceId, string DisplayName, string Thumbprint);

    /// <summary>Metadatos de un archivo en la solicitud de transferencia.</summary>
    public sealed record ItemMeta(
    string ItemId,
    string RelativePath,
    long Size,
    string? Sha256,
    // Task 016.8: 0 = archivo (default, receptores viejos lo ignoran),
    // 1 = mensaje de texto. Campo aditivo: JSON extra en un constructor
    // posicional se ignora en versiones anteriores — degradación elegante.
    int Kind = 0);

    public sealed record TransferRequestPayload(
        string TransferId,
        string DisplayName,
        IReadOnlyList<ItemMeta> Items);

    public sealed record FileStartPayload(string ItemId, string RelativePath, long Size, string? Sha256);

    public sealed record FileEndPayload(string ItemId, long ActualSize, string ActualSha256);

    /// <summary>Offset (en bytes) que el receptor ya tiene del archivo.</summary>
    public sealed record ResumePayload(string ItemId, long Offset);

    /// <summary>
    /// Identidad del iniciador del emparejamiento. La huella es informativa:
    /// la validación real es la del certificado TLS de la conexión, y el
    /// código se deriva de esas huellas vivas, no de este payload.
    /// Port = puerto del SERVICIO del iniciador (para que el receptor pueda
    /// enviarle archivos después; el puerto de la conexión es efímero).
    /// </summary>
    public sealed record PairRequestPayload(
        string DeviceId,
        string DisplayName,
        int Port,
        string Thumbprint);

    public sealed record ErrorPayload(string Code, string Message);

    // ------------------------------------------------------------------
    // Escritura de frames
    // ------------------------------------------------------------------

    /// <summary>Escribe un frame de control (JSON serializado).</summary>
    public static async Task WriteControlAsync(
        Stream stream, byte messageType, object payload, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var payloadBytes = Encoding.UTF8.GetBytes(json);

        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), Magic);
        header[4] = Version;
        header[5] = messageType;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(6, 4), payloadBytes.Length);

        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(payloadBytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Escribe un frame de datos crudos (sin serialización).</summary>
    public static async Task WriteDataAsync(
        Stream stream, ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), Magic);
        header[4] = Version;
        header[5] = MsgFileData;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(6, 4), data.Length);

        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(data, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Lectura de frames
    // ------------------------------------------------------------------

    /// <summary>Lee el siguiente frame del stream. Devuelve (type, payload).</summary>
    public static async Task<(byte Type, byte[] Payload)> ReadFrameAsync(
        Stream stream, CancellationToken ct = default)
    {
        var header = await ReadExactAsync(stream, HeaderSize, ct).ConfigureAwait(false);

        var magic = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
        if (magic != Magic)
        {
            throw new InvalidDataException($"Magic incorrecto: 0x{magic:X8}");
        }

        var version = header[4];
        if (version != Version)
        {
            throw new InvalidDataException($"Versión de protocolo incompatible: {version}");
        }

        var messageType = header[5];
        var payloadLength = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(6, 4));

        if (payloadLength < 0 || payloadLength > 512 * 1024 * 1024) // 512MB máx por frame
        {
            throw new InvalidDataException($"Longitud de payload inválida: {payloadLength}");
        }

        var payload = payloadLength == 0
            ? []
            : await ReadExactAsync(stream, payloadLength, ct).ConfigureAwait(false);

        return (messageType, payload);
    }

    /// <summary>Lee exactamente N bytes del stream (bloquea hasta tenerlos todos).</summary>
    public static async Task<byte[]> ReadExactAsync(
        Stream stream, int count, CancellationToken ct = default)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException($"Stream cerrado tras {offset}/{count} bytes.");
            }
            offset += read;
        }

        return buffer;
    }

    /// <summary>Deserializa un payload JSON de control.</summary>
    public static T? Deserialize<T>(byte[] payload) =>
        JsonSerializer.Deserialize<T>(payload, JsonOpts);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
