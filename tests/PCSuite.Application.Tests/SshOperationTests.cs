using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.Operations;
using PCSuite.Infrastructure.SSH;

namespace PCSuite.Application.Tests;

/// <summary>
/// Las 6 operaciones ssh.* por el puente (Task 009): contrato camelCase,
/// payloads estructurados (no-objeto → MALFORMED_MESSAGE), fallos controlados
/// (NOT_INSTALLED/SSH_TIMEOUT/SSH_FAILED, nunca INTERNAL_ERROR ni stack
/// traces), campos hostiles del payload jamás llegan a los argv y ninguna
/// operación declara elevación. Nada ejecuta procesos reales ni toca el
/// ~/.ssh real del equipo.
/// </summary>
public sealed class SshOperationTests
{
    private const string Ed25519Line =
        "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIP03KRsDPmlF0jrjP2+p5Dg+SAVYQbfCi9S/niKjeZjt dev@pcsuite";

    private const string Ed25519Fingerprint = "SHA256:mAihIkCQImolEaLAOVt+nnjXFutxzX0pUq8bsS9mQBk";

    /// <summary>Payload con campos que NINGUNA operación ssh.* define.</summary>
    private const string HostilePayload =
        """{"command":"calc.exe","args":["--bell"],"path":"C:\\Windows"}""";

    // ------------------------------------------------------------------
    // Contrato camelCase
    // ------------------------------------------------------------------

    [Fact]
    public async Task Detect_WithKeygen_ReturnsAvailable_CamelCase()
    {
        using var fx = SshFixture.Create();

        var response = await Send(fx.Router, "d1", OperationIds.SshDetect);

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.True(data.GetProperty("available").GetBoolean());
        Assert.Equal(fx.KeygenPath, data.GetProperty("path").GetString());
        Assert.Empty(fx.Runner.Requests); // detectar solo resuelve rutas: ningún proceso
    }

    [Fact]
    public async Task Detect_WithoutKeygen_ReturnsUnavailable_NoPathProperty()
    {
        using var fx = SshFixture.Create(withKeygen: false);

        var response = await Send(fx.Router, "d2", OperationIds.SshDetect);

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.False(data.GetProperty("available").GetBoolean());
        Assert.False(data.TryGetProperty("path", out _)); // null omitido
    }

    [Fact]
    public async Task List_WithKeysInSshDirectory_ReturnsKeyArray_CamelCase()
    {
        using var fx = SshFixture.Create();
        fx.WritePublicKey("id_ed25519.pub", Ed25519Line);

        var response = await Send(fx.Router, "l1", OperationIds.SshList);

        Assert.True(response.Success);
        var key = Assert.Single(response.Result!.Value.GetProperty("keys").EnumerateArray());
        Assert.Equal("ssh-ed25519", key.GetProperty("type").GetString());
        Assert.Equal(256, key.GetProperty("bits").GetInt32());
        Assert.Equal(Ed25519Fingerprint, key.GetProperty("fingerprint").GetString());
        Assert.Equal("dev@pcsuite", key.GetProperty("comment").GetString());
        Assert.Equal("OpenSSH", key.GetProperty("format").GetString());
        Assert.False(key.GetProperty("hasPrivate").GetBoolean());
        Assert.Equal(Ed25519Line, key.GetProperty("publicKey").GetString());
        Assert.Empty(fx.Runner.Requests); // listar es parseo .NET puro: ningún proceso
    }

    [Fact]
    public async Task Inspect_WithValidPath_ReturnsKeyInfo_CamelCase()
    {
        using var fx = SshFixture.Create();
        var publicKeyPath = fx.WritePublicKey("id_ed25519.pub", Ed25519Line);

        var response = await Send(
            fx.Router, "i1", OperationIds.SshInspect, JsonSerializer.Serialize(new { path = publicKeyPath }));

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.Equal("Ed25519", data.GetProperty("algorithm").GetString());
        Assert.Equal(Ed25519Fingerprint, data.GetProperty("fingerprint").GetString());
        Assert.Equal(publicKeyPath, data.GetProperty("path").GetString());
        Assert.Equal(Ed25519Line, data.GetProperty("publicKey").GetString());
        Assert.Empty(fx.Runner.Requests); // inspección sin procesos
    }

    [Fact]
    public async Task Generate_WithHostileExtras_RunsFixedArgvAndReturnsKey()
    {
        using var fx = SshFixture.Create(CreateKeyRunner());

        // Extras hostiles (args/command) se descartan al deserializar; directory
        // y comment son campos legítimos del payload.
        var payloadJson = JsonSerializer.Serialize(new
        {
            algorithm = "ed25519",
            comment = "dev@pcsuite",
            directory = fx.SshDirectory,
            args = new[] { "--bell", "-o", "ProxyCommand=calc" },
            command = "calc.exe",
        });

        var response = await Send(fx.Router, "g1", OperationIds.SshGenerate, payloadJson);

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.True(data.GetProperty("succeeded").GetBoolean());
        Assert.Equal(Ed25519Fingerprint, data.GetProperty("key").GetProperty("fingerprint").GetString());
        Assert.Equal(Path.Combine(fx.SshDirectory, "id_ed25519"), data.GetProperty("privateKeyPath").GetString());

        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal(fx.KeygenPath, request.FileName);
        Assert.Equal(
            new[]
            {
                "-q", "-t", "ed25519", "-C", "dev@pcsuite",
                "-f", Path.Combine(fx.SshDirectory, "id_ed25519"), "-N", string.Empty,
            },
            request.Arguments); // argv fijo: nada de args/--bell/ProxyCommand
    }

    [Fact]
    public async Task Import_WithValidKey_WritesFile_CamelCase()
    {
        using var fx = SshFixture.Create();
        var payloadJson =
            JsonSerializer.Serialize(new { key = Ed25519Line, directory = fx.SshDirectory });

        var response = await Send(fx.Router, "m1", OperationIds.SshImportPublicKey, payloadJson);

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.EndsWith(".pub", data.GetProperty("path").GetString(), StringComparison.Ordinal);
        Assert.Equal(Ed25519Fingerprint, data.GetProperty("key").GetProperty("fingerprint").GetString());
        Assert.True(File.Exists(data.GetProperty("path").GetString()));
        Assert.Empty(fx.Runner.Requests); // importar escribe en disco: ningún proceso
    }

    [Fact]
    public async Task OpenDirectory_CreatesFolderAndOpensExplorer_FixedArgv()
    {
        using var fx = SshFixture.Create();

        var response = await Send(fx.Router, "o1", OperationIds.SshOpenDirectory);

        Assert.True(response.Success);
        Assert.Equal(fx.SshDirectory, response.Result!.Value.GetProperty("path").GetString());
        Assert.True(Directory.Exists(fx.SshDirectory));

        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal("explorer.exe", request.FileName);
        Assert.Equal(new[] { fx.SshDirectory }, request.Arguments);
        Assert.True(request.Interactive);
    }

    // ------------------------------------------------------------------
    // Payload estructurado: no-objeto → MALFORMED_MESSAGE
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(OperationIds.SshGenerate, "\"ed25519\"")]
    [InlineData(OperationIds.SshInspect, "[\"id_ed25519.pub\"]")]
    [InlineData(OperationIds.SshImportPublicKey, "123")]
    [InlineData(OperationIds.SshList, "\"--all\"")]
    public async Task NonObjectPayload_SurfacesMalformedMessage(string operation, string payloadJson)
    {
        using var fx = SshFixture.Create();

        var response = await Send(fx.Router, "x1", operation, payloadJson);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, response.Error!.Code);
        Assert.Equal("El payload debe ser un objeto JSON.", response.Error.Message);
        Assert.Empty(fx.Runner.Requests); // ni se intenta lanzar ssh-keygen
    }

    // ------------------------------------------------------------------
    // Errores controlados (nunca INTERNAL_ERROR para fallos de dominio)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Generate_WithoutKeygen_SurfacesNotInstalled()
    {
        using var fx = SshFixture.Create(withKeygen: false);
        var payloadJson = JsonSerializer.Serialize(new { algorithm = "ed25519", directory = fx.SshDirectory });

        var response = await Send(fx.Router, "f1", OperationIds.SshGenerate, payloadJson);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.NotInstalled, response.Error!.Code);
        Assert.DoesNotContain("   at ", response.Error.Message);
        Assert.Empty(fx.Runner.Requests);
    }

    [Fact]
    public async Task Generate_WhenKeygenTimesOut_SurfacesSshTimeout()
    {
        using var fx = SshFixture.Create(
            FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(10))));
        var payloadJson = JsonSerializer.Serialize(new { algorithm = "ed25519", directory = fx.SshDirectory });

        var response = await Send(fx.Router, "f2", OperationIds.SshGenerate, payloadJson);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.SshTimeout, response.Error!.Code);
        Assert.DoesNotContain("   at ", response.Error.Message);
    }

    [Fact]
    public async Task Generate_InvalidAlgorithm_SurfacesSshFailed_WithoutProcess()
    {
        using var fx = SshFixture.Create();
        var payloadJson = JsonSerializer.Serialize(new { algorithm = "dsa; calc.exe", directory = fx.SshDirectory });

        var response = await Send(fx.Router, "f3", OperationIds.SshGenerate, payloadJson);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.SshFailed, response.Error!.Code);
        Assert.DoesNotContain("   at ", response.Error.Message);
        Assert.Empty(fx.Runner.Requests); // allow-list rechaza ANTES de lanzar nada
    }

    [Fact]
    public async Task Inspect_MissingFile_SurfacesSshFailed_NoStackTrace()
    {
        using var fx = SshFixture.Create();
        var missing = Path.Combine(fx.SshDirectory, "no-existe.pub");
        var payloadJson = JsonSerializer.Serialize(new { path = missing });

        var response = await Send(fx.Router, "f4", OperationIds.SshInspect, payloadJson);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.SshFailed, response.Error!.Code);
        Assert.Contains("no existe", response.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", response.Error.Message);
    }

    // ------------------------------------------------------------------
    // §12/§53: campos hostiles del payload jamás llegan a los argv
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(OperationIds.SshDetect)]
    [InlineData(OperationIds.SshList)]
    [InlineData(OperationIds.SshOpenDirectory)]
    public async Task HostilePayload_ExtraFieldsIgnored_DefaultsStillUsed(string operation)
    {
        using var fx = SshFixture.Create();

        var response = await Send(fx.Router, "h1", operation, HostilePayload);

        Assert.True(response.Success);
        switch (operation)
        {
            case OperationIds.SshDetect:
                Assert.Empty(fx.Runner.Requests); // ninguna invocación
                break;
            case OperationIds.SshList:
                Assert.Empty(fx.Runner.Requests); // parseo .NET puro
                break;
            default:
                // Abre ~/.ssh (no el path hostil) con argv fijo.
                Assert.Equal(fx.SshDirectory, response.Result!.Value.GetProperty("path").GetString());
                var request = Assert.Single(fx.Runner.Requests);
                Assert.Equal("explorer.exe", request.FileName);
                Assert.Equal(new[] { fx.SshDirectory }, request.Arguments);
                break;
        }
    }

    // ------------------------------------------------------------------
    // Metadato de elevación: Task 009 jamás eleva
    // ------------------------------------------------------------------

    [Fact]
    public void AllSshOperations_NeverRequireElevation()
    {
        using var fx = SshFixture.Create();
        SshOperation[] operations =
        [
            new SshDetectOperation(fx.Service),
            new SshListOperation(fx.Service),
            new SshGenerateOperation(fx.Service),
            new SshInspectOperation(fx.Service),
            new SshImportPublicKeyOperation(fx.Service),
            new SshOpenDirectoryOperation(fx.Service),
        ];

        Assert.Equal(6, operations.Select(op => op.Id).Distinct().Count());
        Assert.All(operations, op => Assert.False(op.RequiresElevation));
    }

    /// <summary>
    /// Simula a ssh-keygen: crea el par indicado en el argv -f recibido
    /// (privada de contenido irrelevante + .pub con la línea del fixture).
    /// </summary>
    private static FakeProcessRunner CreateKeyRunner() =>
        new((request, _) =>
        {
            var fileIndex = request.Arguments.ToList().IndexOf("-f");
            File.WriteAllText(request.Arguments[fileIndex + 1], "PRIVATE");
            File.WriteAllText(request.Arguments[fileIndex + 1] + ".pub", Ed25519Line);
            return Task.FromResult(new ProcessResult { ExitCode = 0 });
        });

    private static Task<BridgeResponse> Send(
        CommandRouter router,
        string id,
        string operation,
        string? payloadJson = null)
    {
        var payload = payloadJson is null ? string.Empty : $",\"payload\":{payloadJson}";
        return router.RouteAsync(
            $"{{\"id\":\"{id}\",\"operation\":\"{operation}\"{payload}}}");
    }
}

/// <summary>
/// SshKeyService + router para los tests de las operaciones ssh.*: dos carpetas
/// temporales (PATH con ssh-keygen.exe falso opcional y un ~ falso para
/// ~/.ssh); los tests jamás tocan el equipo real.
/// </summary>
internal sealed class SshFixture : IDisposable
{
    private readonly TempExecutablePath _path; // PATH inyectada (ssh-keygen.exe falso)
    private readonly TempExecutablePath _home; // perfil de usuario falso

    private SshFixture(
        TempExecutablePath path,
        TempExecutablePath home,
        string? keygenPath,
        FakeProcessRunner runner,
        SshKeyService service,
        CommandRouter router)
    {
        _path = path;
        _home = home;
        KeygenPath = keygenPath;
        Runner = runner;
        Service = service;
        Router = router;
    }

    /// <summary>Ruta del ssh-keygen.exe falso, o null si withKeygen es false.</summary>
    public string? KeygenPath { get; }

    public FakeProcessRunner Runner { get; }

    public SshKeyService Service { get; }

    /// <summary>Router con las 6 operaciones ssh.* que registra MainForm.</summary>
    public CommandRouter Router { get; }

    public string Home => _home.DirectoryPath;

    public string SshDirectory => Path.Combine(Home, ".ssh");

    public static SshFixture Create(FakeProcessRunner? runner = null, bool withKeygen = true)
    {
        var path = new TempExecutablePath();
        var home = new TempExecutablePath();
        var keygenPath = withKeygen ? path.Add("ssh-keygen.exe") : null;
        runner ??= FakeProcessRunner.Returning(string.Empty);

        var service = new SshKeyService(
            new ExecutableLocator(path.DirectoryPath),
            runner,
            homeDirectory: home.DirectoryPath,
            keygenDirectories: Array.Empty<string>());

        var router = new CommandRouter(
            new IOperation[]
            {
                new SshDetectOperation(service),
                new SshListOperation(service),
                new SshGenerateOperation(service),
                new SshInspectOperation(service),
                new SshImportPublicKeyOperation(service),
                new SshOpenDirectoryOperation(service),
            },
            NullLogger<CommandRouter>.Instance);

        return new SshFixture(path, home, keygenPath, runner, service, router);
    }

    /// <summary>Crea ~/.ssh si falta y escribe un archivo; devuelve su ruta absoluta.</summary>
    public string WritePublicKey(string fileName, string content)
    {
        Directory.CreateDirectory(SshDirectory);
        var path = Path.Combine(SshDirectory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        _path.Dispose();
        _home.Dispose();
    }
}
