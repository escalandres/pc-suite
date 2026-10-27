using System.Security.Cryptography;
using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.Files;

namespace PCSuite.Infrastructure.Operations;

// Task 034: las 5 operaciones cipher.* del puente (mismo patrón que
// PgpOperations): payloads estructurados camelCase, fallos controlados
// CIPHER_FAILED con mensajes en español aptos para la UI (nunca stack
// traces) y cero elevación — todo es .NET puro en proceso. Sin ILogger
// a propósito: contraseñas y claves jamás se registran.
//
// El contrato de encode/xor lleva "direction" (encode|decode /
// encrypt|decrypt): el frontend pide la dirección explícita porque
// formatos como Base64/Hex son ambiguos por sí solos.

/// <summary>
/// Base de las operaciones cipher.*: traducción de los fallos del servicio a
/// errores controlados (código + mensaje aptos para la UI, sin stack traces).
/// </summary>
public abstract class CipherOperation : IOperation
{
    protected CipherOperation(CipherService service) =>
        Service = service ?? throw new ArgumentNullException(nameof(service));

    protected CipherService Service { get; }

    public abstract string Id { get; }

    /// <summary>Task 034 jamás eleva: crypto .NET en el proceso actual.</summary>
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        // Convención del puente: payload no-objeto → MALFORMED_MESSAGE
        // (mismo texto que pgp.*/ssh.*), distinto de un fallo del servicio.
        if (context.Payload is not null
            && context.Payload.Value.ValueKind != JsonValueKind.Object
            && context.Payload.Value.ValueKind != JsonValueKind.Null)
        {
            return OperationResult.Fail(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON.");
        }

        try
        {
            var data = await ExecuteCoreAsync(context, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(data);
        }
        catch (NotSupportedException exception)
        {
            // Mensajes del servicio, ya en español y aptos para la UI:
            // algoritmo desconocido, contraseña vacía, descifrado fallido…
            return OperationResult.Fail(BridgeErrorCodes.CipherFailed, exception.Message);
        }
        catch (FormatException)
        {
            return OperationResult.Fail(BridgeErrorCodes.CipherFailed, "La entrada no es válida para el formato esperado.");
        }
        catch (ArgumentException)
        {
            return OperationResult.Fail(BridgeErrorCodes.CipherFailed, "La entrada no es válida para el algoritmo elegido.");
        }
        catch (CryptographicException)
        {
            return OperationResult.Fail(BridgeErrorCodes.CipherFailed, "No se pudo descifrar con esos datos.");
        }
    }

    protected abstract Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken);

    /// <summary>Payload no-objeto → MALFORMED_MESSAGE (mismo texto que pgp.*).</summary>
    protected static NotSupportedException MalformedPayload() =>
        new("El payload debe ser un objeto JSON.");
}

public sealed class CipherEncodeOperation : CipherOperation
{
    public CipherEncodeOperation(CipherService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.CipherEncode;

    protected override Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out CipherEncodePayload payload))
        {
            throw MalformedPayload();
        }

        var input = payload.Input ?? string.Empty;
        var algorithm = payload.Algorithm ?? string.Empty;
        var output = payload.Direction == "decode"
            ? Service.Decode(algorithm, input, payload.Shift)
            : Service.Encode(algorithm, input, payload.Shift);

        return Task.FromResult(BridgeJson.ToJson(new CipherTextResult { Output = output }));
    }
}

public sealed class CipherHashOperation : CipherOperation
{
    public CipherHashOperation(CipherService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.CipherHash;

    protected override Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out CipherHashPayload payload))
        {
            throw MalformedPayload();
        }

        var result = Service.HashText(payload.Input ?? string.Empty);
        return Task.FromResult(BridgeJson.ToJson(result));
    }
}

public sealed class CipherEncryptAesOperation : CipherOperation
{
    public CipherEncryptAesOperation(CipherService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.CipherEncryptAes;

    protected override Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out CipherAesPayload payload))
        {
            throw MalformedPayload();
        }

        var output = Service.EncryptAes(payload.Input ?? string.Empty, payload.Password ?? string.Empty);
        return Task.FromResult(BridgeJson.ToJson(new CipherTextResult { Output = output }));
    }
}

public sealed class CipherDecryptAesOperation : CipherOperation
{
    public CipherDecryptAesOperation(CipherService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.CipherDecryptAes;

    protected override Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out CipherAesPayload payload))
        {
            throw MalformedPayload();
        }

        var output = Service.DecryptAes(payload.Input ?? string.Empty, payload.Password ?? string.Empty);
        return Task.FromResult(BridgeJson.ToJson(new CipherTextResult { Output = output }));
    }
}

public sealed class CipherXorOperation : CipherOperation
{
    public CipherXorOperation(CipherService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.CipherXor;

    protected override Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out CipherXorPayload payload))
        {
            throw MalformedPayload();
        }

        var output = payload.Direction == "decrypt"
            ? Service.XorDecrypt(payload.Input ?? string.Empty, payload.Key ?? string.Empty)
            : Service.Xor(payload.Input ?? string.Empty, payload.Key ?? string.Empty);

        return Task.FromResult(BridgeJson.ToJson(new CipherTextResult { Output = output }));
    }
}
