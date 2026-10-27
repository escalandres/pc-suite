using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Application;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.Files;
using PCSuite.Infrastructure.Operations;

namespace PCSuite.Application.Tests;

/// <summary>
/// Las 9 operaciones files.*/cipher.* por el puente (Tasks 033/034): el
/// router responde cada id (regresión del bug 2026-10-26 — "Unknown
/// operation 'cipher.encode'" porque las ops nunca se registraron), el
/// contrato es camelCase, los payloads no-objeto → MALFORMED_MESSAGE, los
/// fallos llegan controlados (FILES_FAILED/CIPHER_FAILED, sin stack traces)
/// y las contraseñas jamás se reflejan en los mensajes de error.
///
/// El último test es la guardia de la CLASE de bug: toda constante
/// OperationIds.Files*/Cipher* (esteganografía aparte, documentada como no
/// implementada) debe tener una clase IOperation real — un contrato sin
/// operación vuelve a fallar exactamente como lo reportó el usuario.
/// </summary>
public sealed class FileCipherOperationTests : IDisposable
{
    private const string MarkerPassword = "MARCADOR-SECRETO-QUE-NUNCA-APARECE";

    private readonly TempExecutablePath _temp = new();
    private readonly CommandRouter _router;

    public FileCipherOperationTests()
    {
        var fileSecurityService = new FileSecurityService();
        var cipherService = new CipherService();

        _router = new CommandRouter(
            new IOperation[]
            {
                new FilesEncryptOperation(fileSecurityService),
                new FilesDecryptOperation(fileSecurityService),
                new FilesShredOperation(fileSecurityService),
                new FilesHashOperation(fileSecurityService),
                new CipherEncodeOperation(cipherService),
                new CipherHashOperation(cipherService),
                new CipherEncryptAesOperation(cipherService),
                new CipherDecryptAesOperation(cipherService),
                new CipherXorOperation(cipherService),
            },
            NullLogger<CommandRouter>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    // ------------------------------------------------------------------
    // cipher.encode — la operación que disparó el reporte del usuario
    // ------------------------------------------------------------------

    [Fact]
    public async Task CipherEncode_Responds_InsteadOfOperationNotFound()
    {
        var response = await Send(
            _router,
            "e1",
            OperationIds.CipherEncode,
            JsonSerializer.Serialize(new { algorithm = "base64", input = "PCSuite", direction = "encode" }));

        Assert.True(response.Success);
        Assert.Equal(
            Convert.ToBase64String("PCSuite"u8),
            response.Result!.Value.GetProperty("output").GetString());
    }

    [Fact]
    public async Task CipherEncode_DecodeDirection_AppliesInverseShift()
    {
        var response = await Send(
            _router,
            "e2",
            OperationIds.CipherEncode,
            JsonSerializer.Serialize(new { algorithm = "caesar", input = "fgh", shift = 5, direction = "decode" }));

        Assert.True(response.Success);
        Assert.Equal("abc", response.Result!.Value.GetProperty("output").GetString());
    }

    [Fact]
    public async Task CipherEncode_UnknownAlgorithm_FailsControlled()
    {
        var response = await Send(
            _router,
            "e3",
            OperationIds.CipherEncode,
            JsonSerializer.Serialize(new { algorithm = "no-existe", input = "x", direction = "encode" }));

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.CipherFailed, response.Error!.Code);
        Assert.DoesNotContain("   at ", response.Error.Message);
    }

    [Fact]
    public async Task CipherEncode_NonObjectPayload_IsMalformedMessage()
    {
        var response = await Send(
            _router,
            "e4",
            OperationIds.CipherEncode,
            "\"un string\"");

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, response.Error!.Code);
    }

    // ------------------------------------------------------------------
    // cipher.hash
    // ------------------------------------------------------------------

    [Fact]
    public async Task CipherHash_ReturnsFourHashesInCamelCase()
    {
        var response = await Send(
            _router,
            "h1",
            OperationIds.CipherHash,
            JsonSerializer.Serialize(new { input = "hola puente" }));

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.Matches("^[0-9a-f]{64}$", data.GetProperty("sha256").GetString());
        Assert.Matches("^[0-9a-f]{40}$", data.GetProperty("sha1").GetString());
        Assert.Matches("^[0-9a-f]{32}$", data.GetProperty("md5").GetString());
        Assert.Matches("^[0-9a-f]{128}$", data.GetProperty("sha512").GetString());
    }

    // ------------------------------------------------------------------
    // cipher.encryptAes / cipher.decryptAes / cipher.xor
    // ------------------------------------------------------------------

    [Fact]
    public async Task CipherAes_RoundTripThroughBridge()
    {
        var encrypted = await Send(
            _router,
            "a1",
            OperationIds.CipherEncryptAes,
            JsonSerializer.Serialize(new { input = "mensaje del puente", password = "clave-1" }));
        Assert.True(encrypted.Success);
        var ciphertext = encrypted.Result!.Value.GetProperty("output").GetString();
        Assert.NotNull(ciphertext);
        Assert.NotEmpty(ciphertext);

        var decrypted = await Send(
            _router,
            "a2",
            OperationIds.CipherDecryptAes,
            JsonSerializer.Serialize(new { input = ciphertext, password = "clave-1" }));
        Assert.True(decrypted.Success);
        Assert.Equal("mensaje del puente", decrypted.Result!.Value.GetProperty("output").GetString());
    }

    [Fact]
    public async Task CipherAes_WrongPassword_FailsWithoutLeakingSecrets()
    {
        var encrypted = await Send(
            _router,
            "a3",
            OperationIds.CipherEncryptAes,
            JsonSerializer.Serialize(new { input = "secreto", password = MarkerPassword }));
        Assert.True(encrypted.Success);
        var ciphertext = encrypted.Result!.Value.GetProperty("output").GetString();

        var failed = await Send(
            _router,
            "a4",
            OperationIds.CipherDecryptAes,
            JsonSerializer.Serialize(new { input = ciphertext, password = "otra-clave" }));

        Assert.False(failed.Success);
        Assert.Equal(BridgeErrorCodes.CipherFailed, failed.Error!.Code);
        Assert.DoesNotContain(MarkerPassword, failed.Error.Message);
        Assert.DoesNotContain("   at ", failed.Error.Message);
    }

    [Fact]
    public async Task CipherXor_BothDirections_RoundTrip()
    {
        // La dirección viaja en el payload: cifrar devuelve base64,
        // descifrar devuelve texto — sin ella la op no puede distinguir.
        var encrypted = await Send(
            _router,
            "x1",
            OperationIds.CipherXor,
            JsonSerializer.Serialize(new { input = "texto xor", key = "llave", direction = "encrypt" }));
        Assert.True(encrypted.Success);
        var base64 = encrypted.Result!.Value.GetProperty("output").GetString();
        Assert.NotNull(base64);
        Assert.NotEmpty(base64);

        var decrypted = await Send(
            _router,
            "x2",
            OperationIds.CipherXor,
            JsonSerializer.Serialize(new { input = base64, key = "llave", direction = "decrypt" }));
        Assert.True(decrypted.Success);
        Assert.Equal("texto xor", decrypted.Result!.Value.GetProperty("output").GetString());
    }

    // ------------------------------------------------------------------
    // files.* — ciclo completo por el puente
    // ------------------------------------------------------------------

    [Fact]
    public async Task Files_EncryptHashDecrypt_RoundTripInCamelCase()
    {
        var inputPath = Path.Combine(_temp.DirectoryPath, "doc.txt");
        await File.WriteAllTextAsync(inputPath, "contenido del documento");

        var encrypted = await Send(
            _router,
            "f1",
            OperationIds.FilesEncrypt,
            JsonSerializer.Serialize(new { inputPath, password = "clave-archivos" }));
        Assert.True(encrypted.Success);
        var enc = encrypted.Result!.Value;
        var encPath = enc.GetProperty("outputPath").GetString();
        Assert.EndsWith(".pcsuite-enc", encPath);
        Assert.Matches("^[0-9a-f]{64}$", enc.GetProperty("sha256").GetString());

        // Hash del archivo encriptado: crc32 llega como NÚMERO (uint).
        var hashed = await Send(
            _router,
            "f2",
            OperationIds.FilesHash,
            JsonSerializer.Serialize(new { path = encPath }));
        Assert.True(hashed.Success);
        var hash = hashed.Result!.Value;
        Assert.Equal(JsonValueKind.Number, hash.GetProperty("crc32").ValueKind);
        Assert.Matches("^[0-9a-f]{64}$", hash.GetProperty("sha256").GetString());

        var decrypted = await Send(
            _router,
            "f3",
            OperationIds.FilesDecrypt,
            JsonSerializer.Serialize(new { inputPath = encPath, password = "clave-archivos" }));
        Assert.True(decrypted.Success);
        var decPath = decrypted.Result!.Value.GetProperty("outputPath").GetString();
        Assert.Equal("contenido del documento", await File.ReadAllTextAsync(decPath!));
    }

    [Fact]
    public async Task Files_WrongPassword_FailsControlledWithoutSecrets()
    {
        var inputPath = Path.Combine(_temp.DirectoryPath, "protegido.txt");
        await File.WriteAllTextAsync(inputPath, "datos");

        var encrypted = await Send(
            _router,
            "f4",
            OperationIds.FilesEncrypt,
            JsonSerializer.Serialize(new { inputPath, password = MarkerPassword }));
        Assert.True(encrypted.Success);
        var encPath = encrypted.Result!.Value.GetProperty("outputPath").GetString();

        var failed = await Send(
            _router,
            "f5",
            OperationIds.FilesDecrypt,
            JsonSerializer.Serialize(new { inputPath = encPath, password = "incorrecta" }));

        Assert.False(failed.Success);
        Assert.Equal(BridgeErrorCodes.FilesFailed, failed.Error!.Code);
        Assert.DoesNotContain(MarkerPassword, failed.Error.Message);
        Assert.DoesNotContain("   at ", failed.Error.Message);
    }

    [Fact]
    public async Task Files_NonObjectPayload_IsMalformedMessage()
    {
        var response = await Send(
            _router,
            "f6",
            OperationIds.FilesHash,
            "12345");

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, response.Error!.Code);
    }

    // ------------------------------------------------------------------
    // Guardia de la clase de bug: contrato sin operación
    // ------------------------------------------------------------------

    [Fact]
    public void EveryFilesAndCipherOperationId_HasAnIOperationImplementation()
    {
        var assembly = typeof(PgpEncryptOperation).Assembly;
        object[] services = [new FileSecurityService(), new CipherService()];

        var implemented = new HashSet<string>();
        foreach (var type in assembly.GetTypes())
        {
            if (type is not { IsClass: true, IsAbstract: false } || !typeof(IOperation).IsAssignableFrom(type))
            {
                continue;
            }

            foreach (var ctor in type.GetConstructors())
            {
                var parameters = ctor.GetParameters();
                if (parameters.Length != 1)
                {
                    continue;
                }

                var match = services.FirstOrDefault(s => parameters[0].ParameterType.IsInstanceOfType(s));
                if (match is null)
                {
                    continue;
                }

                try
                {
                    if (Activator.CreateInstance(type, match) is IOperation op)
                    {
                        implemented.Add(op.Id);
                    }
                }
                catch (ArgumentException)
                {
                    // Otras ops con firmas similares: no son de este módulo.
                }
            }
        }

        // files.stegoEmbed/stegoExtract (Task 033.2) quedan FUERA a propósito:
        // la esteganografía no está implementada y no hay UI que las invoque.
        Assert.Superset(
            new HashSet<string>
            {
                OperationIds.FilesEncrypt,
                OperationIds.FilesDecrypt,
                OperationIds.FilesShred,
                OperationIds.FilesHash,
                OperationIds.CipherEncode,
                OperationIds.CipherHash,
                OperationIds.CipherEncryptAes,
                OperationIds.CipherDecryptAes,
                OperationIds.CipherXor,
            },
            implemented);
    }

    // ------------------------------------------------------------------

    private static async Task<BridgeResponse> Send(
        CommandRouter router,
        string id,
        string operation,
        string? payloadJson = null)
    {
        var payload = payloadJson is null ? string.Empty : $",\"payload\":{payloadJson}";
        return await router.RouteAsync(
            $"{{\"id\":\"{id}\",\"operation\":\"{operation}\"{payload}}}");
    }
}
