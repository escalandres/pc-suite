using System.Buffers.Binary;
using System.Text;
using PCSuite.Infrastructure.LocalShare;

namespace PCSuite.Application.Tests;

/// <summary>
/// Tests del wire protocol de Local Share (ShareProtocol):
/// encoding/decoding de frames, límites, magic, version.
/// </summary>
public class ShareProtocolTests
{
    // ------------------------------------------------------------------
    // Round-trip: escribir y leer frames
    // ------------------------------------------------------------------

    [Fact]
    public async Task WriteAndRead_ControlMessage_RoundTrips()
    {
        using var stream = new MemoryStream();

        var payload = new ShareProtocol.HelloPayload("device-123", "Mi PC", "abc123");
        await ShareProtocol.WriteControlAsync(stream, ShareProtocol.MsgHello, payload);

        stream.Position = 0;
        var (type, bytes) = await ShareProtocol.ReadFrameAsync(stream);

        Assert.Equal(ShareProtocol.MsgHello, type);
        var decoded = ShareProtocol.Deserialize<ShareProtocol.HelloPayload>(bytes);
        Assert.NotNull(decoded);
        Assert.Equal("device-123", decoded.DeviceId);
        Assert.Equal("Mi PC", decoded.DisplayName);
        Assert.Equal("abc123", decoded.Thumbprint);
    }

    [Fact]
    public async Task WriteAndRead_DataFrame_RoundTrips()
    {
        using var stream = new MemoryStream();
        var data = new byte[] { 1, 2, 3, 4, 5, 0xFF, 0x00, 0xAB };
        await ShareProtocol.WriteDataAsync(stream, data);

        stream.Position = 0;
        var (type, payload) = await ShareProtocol.ReadFrameAsync(stream);

        Assert.Equal(ShareProtocol.MsgFileData, type);
        Assert.Equal(data, payload);
    }

    // ------------------------------------------------------------------
    // Validación de header
    // ------------------------------------------------------------------

    [Fact]
    public async Task ReadFrame_WrongMagic_Throws()
    {
        var header = new byte[10];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0xDEADBEEF); // magic incorrecto
        header[4] = 1;
        header[5] = 1;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(6, 4), 0);

        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await ShareProtocol.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task ReadFrame_WrongVersion_Throws()
    {
        var header = new byte[10];
        BinaryPrimitives.WriteUInt32BigEndian(header, ShareProtocol.Magic);
        header[4] = 99; // version incorrecta
        header[5] = 1;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(6, 4), 0);

        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await ShareProtocol.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task ReadFrame_InvalidPayloadLength_Throws()
    {
        var header = new byte[10];
        BinaryPrimitives.WriteUInt32BigEndian(header, ShareProtocol.Magic);
        header[4] = ShareProtocol.Version;
        header[5] = 1;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(6, 4), -1); // negativo

        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await ShareProtocol.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task ReadFrame_EmptyPayload_Ok()
    {
        var header = new byte[10];
        BinaryPrimitives.WriteUInt32BigEndian(header, ShareProtocol.Magic);
        header[4] = ShareProtocol.Version;
        header[5] = ShareProtocol.MsgTransferComplete;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(6, 4), 0);

        using var stream = new MemoryStream(header);
        var (type, payload) = await ShareProtocol.ReadFrameAsync(stream);

        Assert.Equal(ShareProtocol.MsgTransferComplete, type);
        Assert.Empty(payload);
    }

    // ------------------------------------------------------------------
    // Frame grande (datos de archivo)
    // ------------------------------------------------------------------

    [Fact]
    public async Task WriteAndRead_LargeDataChunk_RoundTrips()
    {
        using var stream = new MemoryStream();
        var data = new byte[ShareProtocol.ChunkSize]; // 64KB
        new Random(42).NextBytes(data);

        await ShareProtocol.WriteDataAsync(stream, data);
        stream.Position = 0;

        var (type, payload) = await ShareProtocol.ReadFrameAsync(stream);
        Assert.Equal(ShareProtocol.MsgFileData, type);
        Assert.Equal(data.Length, payload.Length);
        Assert.Equal(data, payload);
    }

    // ------------------------------------------------------------------
    // Múltiples frames en secuencia
    // ------------------------------------------------------------------

    [Fact]
    public async Task ReadFrame_MultipleFrames_SequentialReads()
    {
        using var stream = new MemoryStream();

        await ShareProtocol.WriteControlAsync(stream, ShareProtocol.MsgHello,
            new ShareProtocol.HelloPayload("dev1", "PC1", "hash1"));
        await ShareProtocol.WriteControlAsync(stream, ShareProtocol.MsgHelloAck,
            new ShareProtocol.HelloPayload("dev2", "PC2", "hash2"));
        await ShareProtocol.WriteDataAsync(stream, new byte[] { 10, 20, 30 });

        stream.Position = 0;

        var (t1, p1) = await ShareProtocol.ReadFrameAsync(stream);
        Assert.Equal(ShareProtocol.MsgHello, t1);

        var (t2, p2) = await ShareProtocol.ReadFrameAsync(stream);
        Assert.Equal(ShareProtocol.MsgHelloAck, t2);

        var (t3, p3) = await ShareProtocol.ReadFrameAsync(stream);
        Assert.Equal(ShareProtocol.MsgFileData, t3);
        Assert.Equal(new byte[] { 10, 20, 30 }, p3);
    }

    // ------------------------------------------------------------------
    // EOF: stream cerrado
    // ------------------------------------------------------------------

    [Fact]
    public async Task ReadFrame_StreamClosed_Throws()
    {
        using var stream = new MemoryStream();
        // No escribir nada: EOF inmediato
        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
            await ShareProtocol.ReadFrameAsync(stream));
    }
}
