using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using PCSuite.Core.LocalShare;

namespace PCSuite.Infrastructure.LocalShare;

/// <summary>
/// Cliente de transferencia: conecta al receptor, hace handshake TLS,
/// envía metadatos, transfiere archivos en streaming (chunks de 64KB)
/// y computa SHA-256 en paralelo. Cada archivo es leído en streaming
/// (nunca cargado completo en memoria).
/// </summary>
public sealed class TransferClient
{
    private readonly X509Certificate2 _clientCertificate;
    private readonly ILogger? _logger;

    public TransferClient(X509Certificate2 clientCertificate, ILogger? logger = null)
    {
        _clientCertificate = clientCertificate;
        _logger = logger;
    }

    /// <summary>
    /// Envía archivos a un dispositivo remoto. El flujo:
    /// 1. Conectar TCP → handshake TLS (validación de huella del receptor)
    /// 2. HELLO / HELLO_ACK (intercambio de identidad)
    /// 3. TRANSFER_REQUEST (metadatos de todos los archivos)
    /// 4. Esperar TRANSFER_ACCEPT o TRANSFER_REJECT
    /// 5. Por cada archivo: FILE_START → RESUME → FILE_DATA chunks → FILE_END
    /// 6. TRANSFER_COMPLETE + ACK de fin-a-fin
    /// </summary>
    public async Task SendFilesAsync(
        SharedDevice peer,
        ShareTransfer transfer,
        IReadOnlyList<ShareTransferItem> items,
        string selfDeviceId,
        string selfDisplayName,
        string selfThumbprint,
        long maxBytesPerSecond,
        Func<bool>? isPaused,
        Action<long> onProgress,
        CancellationToken ct = default)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(peer.KnownAddresses[0], peer.Port, ct).ConfigureAwait(false);
        using var sslStream = new SslStream(
            tcp.GetStream(),
            leaveInnerStreamOpen: false,
            (_, cert, _, _) => ValidateServerCertificate(cert, peer.CertificateThumbprint));

        var sslOptions = new SslClientAuthenticationOptions
        {
            ClientCertificates = [_clientCertificate],
            TargetHost = "PCSuite Local Share",
            EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };

        await sslStream.AuthenticateAsClientAsync(sslOptions, ct).ConfigureAwait(false);
        _logger?.LogInformation("TLS establecido con {Host}:{Port}", peer.KnownAddresses[0], peer.Port);

        // 1. HELLO
        await ShareProtocol.WriteControlAsync(
            sslStream,
            ShareProtocol.MsgHello,
            new ShareProtocol.HelloPayload(selfDeviceId, selfDisplayName, selfThumbprint),
            ct).ConfigureAwait(false);

        var (ackType, ackPayload) = await ShareProtocol.ReadFrameAsync(sslStream, ct).ConfigureAwait(false);
        if (ackType != ShareProtocol.MsgHelloAck)
        {
            var error = ShareProtocol.Deserialize<ShareProtocol.ErrorPayload>(ackPayload);
            throw new InvalidOperationException(
                $"Handshake rechazado: {error?.Message ?? ackType.ToString()}");
        }

        var ack = ShareProtocol.Deserialize<ShareProtocol.HelloPayload>(ackPayload);
        _logger?.LogInformation("HELLO_ACK de {Name} ({DeviceId})", ack?.DisplayName, ack?.DeviceId);

        // 2. TRANSFER_REQUEST
        var requestMeta = new ShareProtocol.TransferRequestPayload(
            transfer.TransferId,
            selfDisplayName,
            [.. items.Select(i => new ShareProtocol.ItemMeta(
                i.ItemId,
                i.RelativePath,
                i.ExpectedSize,
                i.ExpectedHash))]);

        await ShareProtocol.WriteControlAsync(
            sslStream,
            ShareProtocol.MsgTransferRequest,
            requestMeta,
            ct).ConfigureAwait(false);

        // 3. Esperar ACCEPT o REJECT
        var (responseType, responsePayload) = await ShareProtocol.ReadFrameAsync(sslStream, ct).ConfigureAwait(false);
        if (responseType == ShareProtocol.MsgTransferReject)
        {
            transfer.Status = TransferStatus.Rejected;
            transfer.CompletedAt = DateTimeOffset.UtcNow;
            return;
        }

        if (responseType != ShareProtocol.MsgTransferAccept)
        {
            var err = ShareProtocol.Deserialize<ShareProtocol.ErrorPayload>(responsePayload);
            throw new InvalidOperationException(
                $"Respuesta inesperada: {err?.Message ?? responseType.ToString()}");
        }

        transfer.Status = TransferStatus.Transferring;
        transfer.StartedAt = DateTimeOffset.UtcNow;

        // 4. Transferir cada archivo en streaming
        var totalTransferred = 0L;
        var sendWatch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var item in items)
        {
            if (ct.IsCancellationRequested)
            {
                await SendCancelAsync(sslStream, transfer.TransferId, CancellationToken.None)
                    .ConfigureAwait(false);
                transfer.Status = TransferStatus.Cancelled;
                transfer.CompletedAt = DateTimeOffset.UtcNow;
                return;
            }

            if (item.LocalSourcePath is null || !File.Exists(item.LocalSourcePath))
            {
                _logger?.LogWarning("Archivo local no encontrado: {Path}", item.LocalSourcePath);
                continue;
            }

            // Computar SHA-256 si no está pre-calculado
            var itemHash = item.ExpectedHash ?? SafeFileWriter.ComputeSha256(item.LocalSourcePath);

            // FILE_START
            await ShareProtocol.WriteControlAsync(
                sslStream,
                ShareProtocol.MsgFileStart,
                new ShareProtocol.FileStartPayload(item.ItemId, item.RelativePath, item.ExpectedSize, itemHash),
                ct).ConfigureAwait(false);

            // v2 (16.7.3): el receptor responde con el offset que ya posee de
            // intentos anteriores. 0 = desde cero; == tamaño = nada que enviar.
            var (resumeType, resumePayloadBytes) = await ShareProtocol.ReadFrameAsync(sslStream, ct).ConfigureAwait(false);
            if (resumeType != ShareProtocol.MsgResume)
            {
                var resumeError = ShareProtocol.Deserialize<ShareProtocol.ErrorPayload>(resumePayloadBytes);
                throw new InvalidOperationException(
                    $"Respuesta inesperada tras FILE_START: {resumeError?.Message ?? resumeType.ToString()}");
            }

            var resume = ShareProtocol.Deserialize<ShareProtocol.ResumePayload>(resumePayloadBytes);
            var resumeOffset = resume?.Offset ?? 0;
            if (resumeOffset < 0 || resumeOffset > item.ExpectedSize)
            {
                throw new InvalidOperationException("El receptor pidió un offset de reanudación inválido.");
            }

            if (resumeOffset > 0)
            {
                item.TransferredBytes = resumeOffset;
                _logger?.LogInformation(
                    "Reanudando {File} desde el byte {Offset} de {Size}",
                    item.RelativePath, resumeOffset, item.ExpectedSize);
            }

            // FILE_DATA: leer en chunks de 64KB desde el offset, escribir,
            // actualizar progreso. totalTransferred cuenta SOLO bytes nuevos:
            // en una reanudación es menor que el tamaño del archivo.
            using var fileStream = File.OpenRead(item.LocalSourcePath);
            fileStream.Seek(resumeOffset, SeekOrigin.Begin);
            var buffer = new byte[ShareProtocol.ChunkSize];
            int bytesRead;
            while ((bytesRead = await fileStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                // Pausa del usuario (16.7.4): congelar el flujo hasta reanudar
                // o cancelar. El Task.Delay responde a la cancelación con OCE.
                if (isPaused is not null)
                {
                    while (isPaused())
                    {
                        await Task.Delay(200, ct).ConfigureAwait(false);
                    }
                }

                await ShareProtocol.WriteDataAsync(
                    sslStream,
                    buffer.AsMemory(0, bytesRead),
                    ct).ConfigureAwait(false);

                totalTransferred += bytesRead;
                item.TransferredBytes += bytesRead;
                transfer.TransferredBytes = totalTransferred;
                onProgress(totalTransferred);

                // Regulación de ancho de banda (16.7.2): pausa proporcional si
                // vamos más rápido que el límite configurado (0 = sin límite).
                var delayMs = ComputeThrottleDelayMs(totalTransferred, maxBytesPerSecond, sendWatch.ElapsedMilliseconds);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
            }

            // FILE_END
            await ShareProtocol.WriteControlAsync(
                sslStream,
                ShareProtocol.MsgFileEnd,
                new ShareProtocol.FileEndPayload(item.ItemId, item.ExpectedSize, itemHash),
                ct).ConfigureAwait(false);

            item.Status = TransferStatus.Verifying;
        }

        // 5. TRANSFER_COMPLETE + confirmación de fin-a-fin (16.7.2)
        await ShareProtocol.WriteControlAsync(
            sslStream,
            ShareProtocol.MsgTransferComplete,
            new { transferId = transfer.TransferId },
            ct).ConfigureAwait(false);

        // El receptor responde ACK solo cuando TODOS los archivos se
        // publicaron con integridad verificada. ERROR = fallo suyo (hash,
        // disco). Cualquier otra cosa o EOF = fallo de conexión.
        var (completeAckType, completeAckPayload) = await ShareProtocol.ReadFrameAsync(sslStream, ct).ConfigureAwait(false);
        if (completeAckType != ShareProtocol.MsgTransferCompleteAck)
        {
            var completeError = ShareProtocol.Deserialize<ShareProtocol.ErrorPayload>(completeAckPayload);
            transfer.Status = TransferStatus.Failed;
            transfer.ErrorMessage = completeError?.Message ?? "El receptor no confirmó la transferencia.";
            transfer.CompletedAt = DateTimeOffset.UtcNow;
            return;
        }

        transfer.Status = TransferStatus.Completed;
        transfer.CompletedAt = DateTimeOffset.UtcNow;
        _logger?.LogInformation(
            "Transferencia {Id} completada: {Bytes} bytes en {Count} archivos",
            transfer.TransferId, totalTransferred, items.Count);
    }

    /// <summary>Resultado del diálogo de emparejamiento desde el iniciador.</summary>
    public sealed record PairClientOutcome(
        bool Accepted,
        string PeerDeviceId,
        string PeerDisplayName,
        string PeerThumbprint);

    /// <summary>
    /// Emparejamiento (comparación numérica, lado iniciador):
    /// 1. Conectar TCP → TLS validando al receptor contra la huella ANUNCIADA
    ///    (TOFU por multicast: falsificable, pero el código de comparación
    ///    detecta un MITM porque las huellas vivas diferirían).
    /// 2. HELLO / HELLO_ACK.
    /// 3. Derivar el código de las dos huellas VIVAS del handshake y
    ///    notificarlo via onCodeReady (la UI lo muestra mientras espera).
    /// 4. PAIR_REQUEST y espera de la decisión del usuario REMOTO (el read
    ///    bloquea hasta que el receptor responde o su timeout expira).
    /// </summary>
    public async Task<PairClientOutcome> PairAsync(
        SharedDevice peer,
        string selfDeviceId,
        string selfDisplayName,
        string selfThumbprint,
        int selfServicePort,
        Action<string>? onCodeReady,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(peer.CertificateThumbprint))
        {
            throw new InvalidOperationException(
                "El dispositivo no tiene huella anunciada: no se puede verificar su identidad.");
        }

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(peer.KnownAddresses[0], peer.Port, ct).ConfigureAwait(false);

        // La huella del servidor tal y como se presentó en el handshake:
        // material vivo para derivar el código (no el anuncio).
        string? liveServerThumbprint = null;
        using var sslStream = new SslStream(
            tcp.GetStream(),
            leaveInnerStreamOpen: false,
            (_, cert, _, _) =>
            {
                var thumb = TlsTransferServer.GetThumbprint(cert);
                if (thumb is not null)
                {
                    liveServerThumbprint = thumb;
                }

                return ValidateServerCertificate(cert, peer.CertificateThumbprint);
            });

        var sslOptions = new SslClientAuthenticationOptions
        {
            ClientCertificates = [_clientCertificate],
            TargetHost = "PCSuite Local Share",
            EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };

        await sslStream.AuthenticateAsClientAsync(sslOptions, ct).ConfigureAwait(false);

        if (liveServerThumbprint is null)
        {
            throw new InvalidOperationException("El receptor no presentó certificado.");
        }

        // 1. HELLO / HELLO_ACK
        await ShareProtocol.WriteControlAsync(
            sslStream,
            ShareProtocol.MsgHello,
            new ShareProtocol.HelloPayload(selfDeviceId, selfDisplayName, selfThumbprint),
            ct).ConfigureAwait(false);

        var (ackType, ackPayload) = await ShareProtocol.ReadFrameAsync(sslStream, ct).ConfigureAwait(false);
        if (ackType != ShareProtocol.MsgHelloAck)
        {
            var error = ShareProtocol.Deserialize<ShareProtocol.ErrorPayload>(ackPayload);
            throw new InvalidOperationException(
                $"Handshake rechazado: {error?.Message ?? ackType.ToString()}");
        }

        var ack = ShareProtocol.Deserialize<ShareProtocol.HelloPayload>(ackPayload);
        if (ack is null || string.IsNullOrEmpty(ack.DeviceId))
        {
            throw new InvalidOperationException("El receptor no identificó su dispositivo.");
        }

        // 2. Código de comparación de las huellas VIVAS: idéntico al que
        //    muestra el receptor (que deriva del MISMO par de huellas).
        var code = PairingCode.Derive(selfThumbprint, liveServerThumbprint);
        onCodeReady?.Invoke(code);

        // 3. PAIR_REQUEST: el receptor mostrará el código a su usuario.
        await ShareProtocol.WriteControlAsync(
            sslStream,
            ShareProtocol.MsgPairRequest,
            new ShareProtocol.PairRequestPayload(selfDeviceId, selfDisplayName, selfServicePort, selfThumbprint),
            ct).ConfigureAwait(false);

        // 4. Decisión del usuario remoto (bloquea hasta que responda).
        var (decisionType, _) = await ShareProtocol.ReadFrameAsync(sslStream, ct).ConfigureAwait(false);
        var accepted = decisionType == ShareProtocol.MsgPairAccept;
        if (decisionType is not (ShareProtocol.MsgPairAccept or ShareProtocol.MsgPairReject))
        {
            throw new InvalidOperationException(
                $"Respuesta inesperada al emparejar: {decisionType}");
        }

        _logger?.LogInformation(
            "Emparejamiento con {Name}: {Result}", ack.DisplayName, accepted ? "aceptado" : "rechazado");

        return new PairClientOutcome(accepted, ack.DeviceId, ack.DisplayName, liveServerThumbprint);
    }

    private static async Task SendCancelAsync(Stream stream, string transferId, CancellationToken ct)
    {
        try
        {
            await ShareProtocol.WriteControlAsync(
                stream,
                ShareProtocol.MsgTransferCancel,
                new { transferId },
                ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: si el stream ya está roto, la cancelación
            // se detecta por la desconexión en el receptor.
        }
    }

    private bool ValidateServerCertificate(X509Certificate? certificate, string? expectedThumbprint)
    {
        if (certificate is null || expectedThumbprint is null)
        {
            return false;
        }

        var certBytes = certificate.Export(X509ContentType.Cert);
        var hash = SHA256.HashData(certBytes);
        var actual = Convert.ToHexStringLower(hash);
        return string.Equals(actual, expectedThumbprint, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// MS de espera para no superar el límite de banda. Pacing proporcional:
    /// el tiempo ideal para los bytes enviados menos el tiempo ya transcurrido.
    /// Tope por chunk para mantener la capacidad de cancelar a tiempo.
    /// </summary>
    internal static int ComputeThrottleDelayMs(
        long totalBytesSent,
        long maxBytesPerSecond,
        long elapsedMs)
    {
        if (maxBytesPerSecond <= 0 || totalBytesSent <= 0)
        {
            return 0;
        }

        var idealMs = totalBytesSent * 1000.0 / maxBytesPerSecond;
        var delayMs = idealMs - elapsedMs;
        if (delayMs < 1)
        {
            return 0;
        }

        // Tope por iteración: nunca esperar más de 2s de golpe.
        return (int)Math.Min(delayMs, 2000);
    }
}
