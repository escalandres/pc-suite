using PCSuite.Contracts;
using PCSuite.Core.Processes;
using PCSuite.Core.SSH;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.SSH;

namespace PCSuite.Application.Tests;

/// <summary>
/// Tests de <see cref="SshKeyService"/>: argv fijo de ssh-keygen, allow-list
/// de algoritmos/tamaños, rutas absolutas, listado/inspección por parseo .NET
/// e importación sin sobrescritura. Nada ejecuta procesos reales (FakeProcessRunner)
/// y todos los directorios son temporales: jamás se toca el ~/.ssh real.
/// </summary>
public sealed class SshKeyServiceTests : IDisposable
{
    private const string Ed25519Line =
        "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIP03KRsDPmlF0jrjP2+p5Dg+SAVYQbfCi9S/niKjeZjt dev@pcsuite";

    private const string Ed25519Fingerprint = "SHA256:mAihIkCQImolEaLAOVt+nnjXFutxzX0pUq8bsS9mQBk";

    private readonly List<string> _tempDirectories = [];

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Temporal huérfano: se ignora.
            }
            catch (UnauthorizedAccessException)
            {
                // Idem.
            }
        }
    }

    // ------------------------------------------------------------------
    // Detección
    // ------------------------------------------------------------------

    [Fact]
    public async Task DetectAsync_FindsSshKeygenOnInjectedPath()
    {
        var bin = CreateDummyKeygenDirectory();
        var service = CreateService(pathVariable: bin);

        var status = await service.DetectAsync();

        Assert.True(status.Available);
        Assert.Equal(Path.Combine(bin, "ssh-keygen.exe"), status.Path);
    }

    [Fact]
    public async Task DetectAsync_WhenMissing_ReturnsUnavailable()
    {
        var service = CreateService();

        var status = await service.DetectAsync();

        Assert.False(status.Available);
        Assert.Null(status.Path);
    }

    // ------------------------------------------------------------------
    // Listado
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListAsync_WhenSshFolderIsMissing_ReturnsEmpty()
    {
        var service = CreateService();

        var keys = await service.ListAsync(null);

        Assert.Empty(keys);
    }

    [Fact]
    public async Task ListAsync_ParsesPubFilesAndSkipsInvalidOnes()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(Path.Combine(directory, "id_ed25519.pub"), Ed25519Line);
        File.WriteAllText(Path.Combine(directory, "broken.pub"), "ssh-ed25519 no-es-base64!! x\n");
        File.WriteAllText(Path.Combine(directory, "config"), "Host *\n  ServerAliveInterval 60\n");
        File.WriteAllText(Path.Combine(directory, "authorized_keys"), "# comentario\n");
        var service = CreateService();

        var keys = await service.ListAsync(directory);

        var key = Assert.Single(keys);
        Assert.Equal("ssh-ed25519", key.Type);
        Assert.Equal(Ed25519Fingerprint, key.Fingerprint);
        Assert.False(key.HasPrivate);
        Assert.Equal(SshKeyParser.FormatOpenSsh, key.Format);
    }

    [Fact]
    public async Task ListAsync_PrivateContainerWithoutPub_ReportsHasPrivate()
    {
        var directory = CreateTempDirectory();
        File.WriteAllBytes(Path.Combine(directory, "id_ed25519"), PrivateContainer(Ed25519Line));
        var service = CreateService();

        var keys = await service.ListAsync(directory);

        var key = Assert.Single(keys);
        Assert.Equal(Ed25519Fingerprint, key.Fingerprint);
        Assert.True(key.HasPrivate);
        Assert.Equal(string.Empty, key.Comment);
    }

    [Fact]
    public async Task ListAsync_PubWithSiblingPrivate_ReportsHasPrivate()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(Path.Combine(directory, "id_ed25519.pub"), Ed25519Line);
        File.WriteAllBytes(Path.Combine(directory, "id_ed25519"), PrivateContainer(Ed25519Line));
        var service = CreateService();

        var keys = await service.ListAsync(directory);

        var key = Assert.Single(keys);
        Assert.True(key.HasPrivate);
    }

    // ------------------------------------------------------------------
    // Inspección
    // ------------------------------------------------------------------

    [Fact]
    public async Task InspectAsync_PubFileWithSiblingPrivate_ReturnsFullInfo()
    {
        var directory = CreateTempDirectory();
        var publicKeyPath = Path.Combine(directory, "id_ed25519.pub");
        File.WriteAllText(publicKeyPath, Ed25519Line);
        File.WriteAllBytes(Path.Combine(directory, "id_ed25519"), PrivateContainer(Ed25519Line));
        var service = CreateService();

        var key = await service.InspectAsync(publicKeyPath);

        Assert.Equal("ssh-ed25519", key.Type);
        Assert.Equal("Ed25519", key.Algorithm);
        Assert.Equal(256, key.Bits);
        Assert.Equal(Ed25519Fingerprint, key.Fingerprint);
        Assert.Equal("dev@pcsuite", key.Comment);
        Assert.Equal(SshKeyParser.FormatOpenSsh, key.Format);
        Assert.Equal(publicKeyPath, key.Path);
        Assert.True(key.HasPrivate);
        Assert.Equal(Ed25519Line, key.PublicKey);
    }

    [Fact]
    public async Task InspectAsync_PrivateContainer_ExtractsEmbeddedPublicKey()
    {
        var path = Path.Combine(CreateTempDirectory(), "id_ed25519");
        File.WriteAllBytes(path, PrivateContainer(Ed25519Line));
        var service = CreateService();

        var key = await service.InspectAsync(path);

        Assert.Equal(Ed25519Fingerprint, key.Fingerprint);
        Assert.True(key.HasPrivate);
        Assert.StartsWith("ssh-ed25519 AAAA", key.PublicKey, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "Indica la ruta")]
    [InlineData("id_ed25519.pub", "absoluta")]
    public async Task InspectAsync_InvalidPath_ThrowsControlledError(string path, string fragment)
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<SshException>(() => service.InspectAsync(path));

        Assert.Equal(BridgeErrorCodes.SshFailed, exception.ErrorCode);
        Assert.Contains(fragment, exception.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectAsync_MissingFile_ThrowsControlledError()
    {
        var service = CreateService();
        var path = Path.Combine(CreateTempDirectory(), "no-existe.pub");

        var exception = await Assert.ThrowsAsync<SshException>(() => service.InspectAsync(path));

        Assert.Equal(BridgeErrorCodes.SshFailed, exception.ErrorCode);
        Assert.Contains("no existe", exception.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectAsync_GarbageFile_ThrowsControlledError()
    {
        var path = Path.Combine(CreateTempDirectory(), "garbage.pub");
        File.WriteAllText(path, "esto no es una clave\n");
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<SshException>(() => service.InspectAsync(path));

        Assert.Equal(BridgeErrorCodes.SshFailed, exception.ErrorCode);
        Assert.Contains("no contiene una clave OpenSSH válida", exception.ErrorMessage, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Generación
    // ------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_UnknownAlgorithm_ThrowsControlledError()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<SshException>(() =>
            service.GenerateAsync(new SshGenerateRequest("dsa", null, "dev", CreateTempDirectory(), null)));

        Assert.Equal(BridgeErrorCodes.SshFailed, exception.ErrorCode);
        Assert.Contains("ed25519, rsa o ecdsa", exception.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rsa", 1234, "2048, 3072 o 4096")]
    [InlineData("ecdsa", 999, "256, 384 o 521")]
    [InlineData("ed25519", 256, "no admite tamaño")]
    public async Task GenerateAsync_InvalidBits_ThrowsControlledError(string algorithm, int bits, string fragment)
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<SshException>(() =>
            service.GenerateAsync(new SshGenerateRequest(algorithm, bits, "dev", CreateTempDirectory(), null)));

        Assert.Equal(BridgeErrorCodes.SshFailed, exception.ErrorCode);
        Assert.Contains(fragment, exception.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_RelativeDirectory_ThrowsControlledError()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<SshException>(() =>
            service.GenerateAsync(new SshGenerateRequest("ed25519", null, "dev", "claves", null)));

        Assert.Equal(BridgeErrorCodes.SshFailed, exception.ErrorCode);
        Assert.Contains("absoluta", exception.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_CommentWithNewline_ThrowsControlledError()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<SshException>(() =>
            service.GenerateAsync(new SshGenerateRequest("ed25519", null, "uno\ndos", CreateTempDirectory(), null)));

        Assert.Equal(BridgeErrorCodes.SshFailed, exception.ErrorCode);
        Assert.Contains("saltos de línea", exception.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_ExistingKey_ReturnsFailureWithoutRunningProcess()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(Path.Combine(directory, "id_ed25519.pub"), Ed25519Line);
        var runner = FakeProcessRunner.Returning(string.Empty);
        var service = CreateService(runner);

        var outcome = await service.GenerateAsync(
            new SshGenerateRequest("ed25519", null, "dev", directory, null));

        Assert.False(outcome.Succeeded);
        Assert.Contains("id_ed25519", outcome.Detail!, StringComparison.Ordinal);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task GenerateAsync_WhenKeygenMissing_ThrowsNotInstalled()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<SshException>(() =>
            service.GenerateAsync(new SshGenerateRequest("ed25519", null, "", CreateTempDirectory(), null)));

        Assert.Equal(BridgeErrorCodes.NotInstalled, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_Success_BuildsFixedArgvAndReturnsParsedKey()
    {
        var directory = CreateTempDirectory();
        var bin = CreateDummyKeygenDirectory();
        var runner = new FakeProcessRunner((request, _) =>
        {
            // Simula a ssh-keygen: crea el par indicado en el -f recibido.
            var fileIndex = request.Arguments.ToList().IndexOf("-f");
            File.WriteAllText(request.Arguments[fileIndex + 1], "PRIVATE");
            File.WriteAllText(request.Arguments[fileIndex + 1] + ".pub", Ed25519Line);
            return Task.FromResult(new ProcessResult { ExitCode = 0 });
        });
        var service = CreateService(runner, pathVariable: bin);

        var outcome = await service.GenerateAsync(
            new SshGenerateRequest("ed25519", null, "dev@pcsuite", directory, null));

        Assert.True(outcome.Succeeded);
        Assert.Equal(Path.Combine(directory, "id_ed25519"), outcome.PrivateKeyPath);
        Assert.Equal(Path.Combine(directory, "id_ed25519.pub"), outcome.PublicKeyPath);
        Assert.NotNull(outcome.Key);
        Assert.Equal(Ed25519Fingerprint, outcome.Key!.Fingerprint);

        var processRequest = Assert.Single(runner.Requests);
        Assert.Equal(Path.Combine(bin, "ssh-keygen.exe"), processRequest.FileName);
        Assert.Equal(
            new[] { "-q", "-t", "ed25519", "-C", "dev@pcsuite", "-f", Path.Combine(directory, "id_ed25519"), "-N", "" },
            processRequest.Arguments);
        Assert.False(processRequest.Interactive);
        Assert.NotNull(processRequest.Timeout);
    }

    [Fact]
    public async Task GenerateAsync_RsaWithoutBits_Uses3072Default()
    {
        var directory = CreateTempDirectory();
        var bin = CreateDummyKeygenDirectory();
        var runner = FakeProcessRunner.Returning(string.Empty);
        var service = CreateService(runner, pathVariable: bin);

        var outcome = await service.GenerateAsync(
            new SshGenerateRequest("rsa", null, "", directory, null));

        Assert.True(outcome.Succeeded);
        var processRequest = Assert.Single(runner.Requests);
        Assert.Equal(
            new[] { "-q", "-t", "rsa", "-b", "3072", "-C", "", "-f", Path.Combine(directory, "id_rsa"), "-N", "" },
            processRequest.Arguments);
    }

    [Fact]
    public async Task GenerateAsync_WhenKeygenTimesOut_ThrowsSshTimeout()
    {
        var bin = CreateDummyKeygenDirectory();
        var runner = FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(5)));
        var service = CreateService(runner, pathVariable: bin);

        var exception = await Assert.ThrowsAsync<SshException>(() =>
            service.GenerateAsync(new SshGenerateRequest("ed25519", null, "", CreateTempDirectory(), null)));

        Assert.Equal(BridgeErrorCodes.SshTimeout, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_WhenKeygenFails_ReturnsSingleLineDetailWithoutPassphrase()
    {
        const string passphrase = "super-secreto";
        var bin = CreateDummyKeygenDirectory();
        var runner = new FakeProcessRunner((_, _) => Task.FromResult(new ProcessResult
        {
            ExitCode = 1,
            StandardError = $"fallo con el passphrase {passphrase}\nsegunda línea",
        }));
        var service = CreateService(runner, pathVariable: bin);

        var outcome = await service.GenerateAsync(
            new SshGenerateRequest("ed25519", null, "", CreateTempDirectory(), passphrase));

        Assert.False(outcome.Succeeded);
        Assert.Equal("ssh-keygen terminó con código 1: fallo con el passphrase ****", outcome.Detail);
        Assert.DoesNotContain(passphrase, outcome.Detail!, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", outcome.Detail!, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Importación de claves públicas
    // ------------------------------------------------------------------

    [Fact]
    public async Task ImportPublicKeyAsync_InvalidKey_ThrowsControlledError()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<SshException>(() =>
            service.ImportPublicKeyAsync(new SshImportRequest("esto no es una clave", "ok.pub", CreateTempDirectory())));

        Assert.Equal(BridgeErrorCodes.SshFailed, exception.ErrorCode);
        Assert.Contains("no es válida", exception.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("..")]
    [InlineData("con espacio.pub")]
    [InlineData("id\\evil.pub")]
    public async Task ImportPublicKeyAsync_UnsafeFileName_ThrowsControlledError(string fileName)
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<SshException>(() =>
            service.ImportPublicKeyAsync(new SshImportRequest(Ed25519Line, fileName, CreateTempDirectory())));

        Assert.Equal(BridgeErrorCodes.SshFailed, exception.ErrorCode);
        Assert.Contains("nombre del archivo", exception.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportPublicKeyAsync_ExistingFile_ThrowsWithoutOverwriting()
    {
        var directory = CreateTempDirectory();
        var target = Path.Combine(directory, "mi-clave.pub");
        File.WriteAllText(target, "original");
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<SshException>(() =>
            service.ImportPublicKeyAsync(new SshImportRequest(Ed25519Line, "mi-clave", directory)));

        Assert.Equal(BridgeErrorCodes.SshFailed, exception.ErrorCode);
        Assert.Equal("original", File.ReadAllText(target));
    }

    [Fact]
    public async Task ImportPublicKeyAsync_WritesCanonicalLineAndDerivesFingerprintName()
    {
        var directory = Path.Combine(CreateTempDirectory(), ".ssh");
        var service = CreateService();

        var outcome = await service.ImportPublicKeyAsync(
            new SshImportRequest("  " + Ed25519Line + "  ", string.Empty, directory));

        var expectedPath = Path.Combine(directory, "imported-mAihIkCQImol.pub");
        Assert.Equal(expectedPath, outcome.Path);
        Assert.True(Directory.Exists(directory));
        Assert.Equal(Ed25519Line + "\n", File.ReadAllText(expectedPath));
        Assert.Equal(Ed25519Fingerprint, outcome.Key.Fingerprint);

        var reparsed = SshKeyParser.TryParsePublicKeyLine(File.ReadAllText(expectedPath));
        Assert.NotNull(reparsed);
        Assert.Equal(Ed25519Fingerprint, reparsed!.Fingerprint);
    }

    // ------------------------------------------------------------------
    // Abrir la carpeta .ssh
    // ------------------------------------------------------------------

    [Fact]
    public async Task OpenSshDirectoryAsync_CreatesFolderAndOpensExplorerWithFixedArgv()
    {
        var home = CreateTempDirectory();
        var runner = FakeProcessRunner.Returning(string.Empty);
        var service = CreateService(runner, homeDirectory: home);

        var directory = await service.OpenSshDirectoryAsync();

        Assert.Equal(Path.Combine(home, ".ssh"), directory);
        Assert.True(Directory.Exists(directory));

        var processRequest = Assert.Single(runner.Requests);
        Assert.Equal("explorer.exe", processRequest.FileName);
        Assert.Equal(new[] { directory }, processRequest.Arguments);
        Assert.True(processRequest.Interactive);
    }

    // ------------------------------------------------------------------
    // Utilidades
    // ------------------------------------------------------------------

    private SshKeyService CreateService(
        FakeProcessRunner? runner = null,
        string? homeDirectory = null,
        string? pathVariable = null,
        IReadOnlyList<string>? keygenDirectories = null)
    {
        // PATH vacía por omisión: ninguna prueba depende del equipo real.
        var locator = new ExecutableLocator(pathVariable ?? string.Empty);
        return new SshKeyService(
            locator,
            runner ?? FakeProcessRunner.Returning(string.Empty),
            logger: null,
            homeDirectory: homeDirectory ?? CreateTempDirectory(),
            keygenDirectories: keygenDirectories ?? [CreateTempDirectory()]);
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pcsuite-ssh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    /// <summary>Carpeta temporal con un ssh-keygen.exe ficticio (solo se usa como ruta: nunca se ejecuta).</summary>
    private string CreateDummyKeygenDirectory()
    {
        var directory = CreateTempDirectory();
        File.WriteAllBytes(Path.Combine(directory, "ssh-keygen.exe"), [0x4D, 0x5A]);
        return directory;
    }

    private static byte[] PrivateContainer(string publicKeyLine)
    {
        var parts = publicKeyLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var blob = Convert.FromBase64String(parts[1]);
        using var stream = new MemoryStream();
        stream.Write(SshKeyParser.OpenSshPrivateKeyMagic);
        WriteString(stream, "none"u8.ToArray());
        WriteString(stream, "none"u8.ToArray());
        WriteString(stream, []);
        WriteU32(stream, 1);
        WriteString(stream, blob);
        return stream.ToArray();
    }

    private static void WriteU32(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteString(Stream stream, byte[] value)
    {
        WriteU32(stream, (uint)value.Length);
        stream.Write(value, 0, value.Length);
    }
}
