using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using PCSuite.Core.PGP;
using PCSuite.Infrastructure.PGP;
using PCSuite.Infrastructure.Updates;

namespace PCSuite.Application.Tests;

/// <summary>
/// Tests E2E del updater (Task 17.3/17.4/17.6): servidor HTTP local que sirve
/// un manifiesto firmado con PGP (clave de test generada por test — jamás la
/// de producción) y artefactos REALES (bytes generados + hash correcto).
/// Los criterios de aceptación de la propuesta §10:
///
/// - Manifiesto con versión mayor → disponible; descarga → firma OK →
///   hash OK → install lanza el archivo verificado (launcher fake registra
///   el intento).
/// - Manifiesto con SHA-256 ALTERADO → la descarga falla, el archivo se
///   BORRA de %TEMP% y InstallAsync jamás se ofrece (lanza/rechaza).
/// - Manifiesto ALTERADO tras firmar / firma ausente → check silencioso,
///   descarga rechazada (17.6: sin firma no hay confianza).
/// - Versión menor o igual → no disponible.
/// - Comparador SemVer (0.5.10 > 0.5.9; jamás lexicográfico).
/// </summary>
public class PcsuiteUpdateServiceTests : IDisposable
{
    private readonly SimpleHttpServer _server = new();

    /// <summary>Keyring PGP de test: firma el manifiesto local (ed25519).</summary>
    private readonly TempExecutablePath _pgpDir = new();

    private readonly PgpService _pgp;

    private readonly PgpKeyInfo _signingKey;

    /// <summary>Verificador con la clave pública de test + TOFU en temporal.</summary>
    private readonly ManifestSignatureVerifier _verifier;

    public PcsuiteUpdateServiceTests()
    {
        _pgp = new PgpService(new PgpKeyringStore(_pgpDir.DirectoryPath));
        _signingKey = _pgp.GenerateKeyAsync(new PgpGenerateKeyRequest(
            "Updater Test", "updater@test.es", PgpAlgorithms.Ed25519, null, null, null))
            .GetAwaiter().GetResult();
        var publicKeyArmored = _pgp.ExportKeyAsync(
            new PgpExportKeyRequest(_signingKey.Fingerprint, false))
            .GetAwaiter().GetResult();
        _verifier = new ManifestSignatureVerifier(
            publicKeyArmored,
            Path.Combine(_pgpDir.DirectoryPath, "tofu-fingerprint.txt"));
    }

    public void Dispose()
    {
        _server.Dispose();
        _pgpDir.Dispose();
    }

    /// <summary>Firma el manifiesto con la clave de test y lo sirve con .sig.</summary>
    private string SignManifest(string manifest)
    {
        return _pgp.SignAsync(
            new PgpSignRequest(manifest, _signingKey.Fingerprint, null))
            .GetAwaiter().GetResult();
    }

    private PcsuiteUpdateService CreateService(
        byte[] setupBytes,
        string manifestVersion,
        string? fakeSha256 = null,
        string currentVersion = "0.4.0",
        List<string>? launches = null)
    {
        var sha = fakeSha256 ?? Convert.ToHexStringLower(SHA256.HashData(setupBytes));
        var manifest = $$"""
        {
          "product": "pcsuite",
          "channel": "stable",
          "version": "{{manifestVersion}}",
          "releasedAt": "2026-10-14T12:00:00Z",
          "releaseNotesUrl": "https://example.com/notes",
          "artifacts": [
            {
              "id": "setup-win64",
              "kind": "installer",
              "label": "Instalador",
              "url": "{{_server.Url}}setup.exe",
              "size": {{setupBytes.Length}},
              "sha256": "{{sha}}"
            }
          ]
        }
        """;
        _server.ManifestContent = manifest;
        _server.SignatureContent = SignManifest(manifest);
        _server.SetupContent = setupBytes;

        return new PcsuiteUpdateService(
            manifestUrl: $"{_server.Url}latest.json",
            launchInstaller: path =>
            {
                launches?.Add(path);
                return true;
            },
            currentVersion: currentVersion,
            signatureVerifier: _verifier);
    }

    [Fact]
    public async Task Check_ConVersionMayor_Disponible()
    {
        var service = CreateService([1, 2, 3], "0.5.0", currentVersion: "0.4.0");

        var check = await service.CheckAsync();

        Assert.True(check.Available);
        Assert.Equal("0.4.0", check.CurrentVersion);
        Assert.Equal("0.5.0", check.LatestVersion);
        Assert.Null(check.Error);
    }

    [Fact]
    public async Task Check_ConVersionMenor_NoDisponible()
    {
        var service = CreateService([1, 2, 3], "0.4.0", currentVersion: "0.5.0");

        var check = await service.CheckAsync();

        Assert.False(check.Available);
        Assert.Null(check.Error);
    }

    [Fact]
    public async Task Check_SinRed_ResultadoSilenciosoNuncaLanza()
    {
        var service = new PcsuiteUpdateService(
            manifestUrl: "http://localhost:1/latest.json", // puerto imposible
            currentVersion: "0.4.0");

        var check = await service.CheckAsync();

        // La app funciona igual: sin dramatismo, sin error en la cara.
        Assert.False(check.Available);
    }

    [Fact]
    public async Task Download_HashCorrecto_InstallLanzaElArchivo()
    {
        var setup = new byte[300 * 1024]; // 300 KB: cruza varios chunks de 64 KB
        Random.Shared.NextBytes(setup);
        var launches = new List<string>();
        var service = CreateService(setup, "0.5.0", launches: launches);

        var progressCalls = 0;
        var path = await service.DownloadAsync((_, _) => progressCalls++);

        Assert.True(File.Exists(path));
        Assert.Equal(setup.Length, new FileInfo(path).Length);
        Assert.True(progressCalls >= 3, $"Debió reportar progreso varias veces (fue {progressCalls}).");

        // Instalar con la MISMA ruta verificada → lanza.
        await service.InstallAsync(path);
        Assert.Single(launches);
        Assert.Equal(path, launches[0]);
    }

    [Fact]
    public async Task Download_HashAlterado_SeBorraYNuncaSeInstala()
    {
        var setup = new byte[100 * 1024];
        Random.Shared.NextBytes(setup);
        var launches = new List<string>();
        // Hash FALSO en el manifiesto: la descarga NO es lo que dice ser.
        var service = CreateService(setup, "0.5.0", fakeSha256: new string('f', 64), launches: launches);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync());

        Assert.Contains("SHA-256", ex.Message);
        Assert.Contains("No se instalará", ex.Message);

        // El archivo se BORRÓ de %TEMP%.
        var tempFiles = Directory.GetFiles(
            Path.GetTempPath(), "PCSuite-Setup-*.exe", SearchOption.TopDirectoryOnly);
        Assert.DoesNotContain(tempFiles, f => File.ReadAllBytes(f).SequenceEqual(setup));
    }

    [Fact]
    public async Task Install_RutaAjena_SeRechaza()
    {
        var setup = new byte[10 * 1024];
        var launches = new List<string>();
        var service = CreateService(setup, "0.5.0", launches: launches);

        // Sin descarga previa en la sesión: CUALQUIER ruta se rechaza.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InstallAsync("C:\\Windows\\evil.exe"));
        Assert.Empty(launches);

        // Con descarga: una ruta DISTINTA a la verificada también se rechaza.
        var verified = await service.DownloadAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InstallAsync("C:\\Windows\\evil.exe"));
        Assert.Empty(launches);

        // La ruta correcta sí lanza.
        await service.InstallAsync(verified);
        Assert.Single(launches);
    }

    // ------------------------------------------------------------------
    // Firma del manifiesto (Task 17.6): alterada o ausente → sin confianza
    // ------------------------------------------------------------------

    [Fact]
    public async Task Check_ConManifiestoAlteradoTrasFirmar_ResultadoSilencioso()
    {
        var service = CreateService([1, 2, 3], "0.5.0", currentVersion: "0.4.0");

        // Un atacante modifica el manifiesto DESPUÉS de que lo firmáramos:
        // la firma sigue servida, pero ya no corresponde al contenido.
        _server.ManifestContent = _server.ManifestContent.Replace("0.5.0", "9.9.9");

        var check = await service.CheckAsync();

        // Silencioso: sin drama, sin error en la cara — pero sin confianza.
        Assert.False(check.Available);
    }

    [Fact]
    public async Task Download_ConManifiestoAlterado_SeRechaza()
    {
        var launches = new List<string>();
        var service = CreateService([1, 2, 3], "0.5.0", launches: launches);

        _server.ManifestContent = _server.ManifestContent.Replace("0.5.0", "9.9.9");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync());

        Assert.Contains("Firma del manifiesto inválida", ex.Message);
        Assert.Empty(launches);
    }

    [Fact]
    public async Task Download_SinFirma_SeRechaza()
    {
        var launches = new List<string>();
        var service = CreateService([1, 2, 3], "0.5.0", launches: launches);

        // Sin latest.json.sig (404): sin firma no hay confianza.
        _server.SignatureContent = null;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync());

        Assert.Contains("Firma del manifiesto inválida", ex.Message);
        Assert.Empty(launches);
    }

    // ------------------------------------------------------------------
    // Comparador SemVer
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("0.5.10", "0.5.9", 1)]  // jamás lexicográfico ("10" < "9")
    [InlineData("0.6.0", "0.5.99", 1)]
    [InlineData("1.0.0", "0.9.9", 1)]
    [InlineData("0.5.0", "0.5.0", 0)]
    [InlineData("0.5.0", "0.5.1", -1)]
    [InlineData("1.0.0-beta", "1.0.0", 0)] // sufijos ignorados por ahora
    public void CompareVersions_SemVer(string a, string b, int expectedSign)
    {
        var result = Math.Sign(PcsuiteUpdateService.CompareVersions(a, b));
        Assert.Equal(expectedSign, result);
    }

    // ------------------------------------------------------------------
    // Servidor HTTP mínimo (no usar en producción: solo tests)
    // ------------------------------------------------------------------

    private sealed class SimpleHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _loop;

        public string ManifestContent { get; set; } = "{}";
        public byte[] SetupContent { get; set; } = [];

        /// <summary>Firma PGP del manifiesto; null → el servidor responde 404.</summary>
        public string? SignatureContent { get; set; }

        public string Url { get; }

        public SimpleHttpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Url = $"http://localhost:{port}/";
            _loop = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch (SocketException)
                {
                    break; // disposed
                }

                _ = Task.Run(() => HandleAsync(client));
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using var _ = client;
            await using var stream = client.GetStream();

            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = System.Text.Encoding.ASCII.GetString(buffer, 0, read);

            if (request.Contains("GET /latest.json.sig"))
            {
                if (SignatureContent is null)
                {
                    await WriteNotFoundAsync(stream);
                    return;
                }
                var body = System.Text.Encoding.UTF8.GetBytes(SignatureContent);
                await WriteResponse(stream, "application/pgp-signature", body);
            }
            else if (request.Contains("GET /latest.json"))
            {
                var body = System.Text.Encoding.UTF8.GetBytes(ManifestContent);
                await WriteResponse(stream, "application/json", body);
            }
            else if (request.Contains("GET /setup.exe"))
            {
                await WriteResponse(stream, "application/octet-stream", SetupContent);
            }
            else
            {
                await WriteNotFoundAsync(stream);
            }
        }

        private static async Task WriteNotFoundAsync(NetworkStream stream)
        {
            var body = System.Text.Encoding.ASCII.GetBytes("404");
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(
                "HTTP/1.1 404 Not Found\r\nContent-Length: 3\r\n\r\n"));
            await stream.WriteAsync(body);
        }

        private static async Task WriteResponse(NetworkStream stream, string contentType, byte[] body)
        {
            var header = System.Text.Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(body);
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { /* best-effort */ }
        }
    }
}
