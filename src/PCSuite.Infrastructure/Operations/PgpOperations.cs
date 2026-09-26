using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.PGP;

namespace PCSuite.Infrastructure.Operations;

// Task 009 (§6): las 8 operaciones pgp.* en un solo archivo (mismo patrón
// que SshOperations): payloads estructurados camelCase validados en el
// servicio y PgpException (controlada) → error del puente; una firma no
// válida es un resultado legítimo (Valid = false), no un error. Todo ocurre
// en proceso con BouncyCastle: sin procesos externos, sin elevación y sin
// ILogger (contraseña, texto plano y clave privada jamás se registran).

/// <summary>
/// Base de las operaciones pgp.* del puente: traducción de
/// <see cref="PgpException"/> a errores controlados (código + mensaje aptos
/// para la UI, nunca stack traces) y mapeo dominio → DTO camelCase.
/// </summary>
public abstract class PgpOperation : IOperation
{
    protected PgpOperation(IPgpService service) =>
        Service = service ?? throw new ArgumentNullException(nameof(service));

    protected IPgpService Service { get; }

    public abstract string Id { get; }

    /// <summary>Task 009 jamás eleva: BouncyCastle trabaja en el proceso actual.</summary>
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        try
        {
            var data = await ExecuteCoreAsync(context, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(data);
        }
        catch (PgpException exception)
        {
            return OperationResult.Fail(exception.ErrorCode, exception.ErrorMessage);
        }
    }

    protected abstract Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken);

    /// <summary>Payload no-objeto → MALFORMED_MESSAGE (mismo texto que ssh.*).</summary>
    protected static PgpException MalformedPayload() =>
        new(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON.");

    protected static JsonElement? ToKey(PgpKeyInfo key) => BridgeJson.ToJson(ToKeyInfo(key));

    protected static JsonElement? ToKeyList(IReadOnlyList<PgpKeyInfo> keys) =>
        BridgeJson.ToJson(new PgpListKeysResult { Keys = keys.Select(ToKeyInfo).ToArray() });

    protected static JsonElement? ToVerify(PgpVerifyOutcome outcome) =>
        BridgeJson.ToJson(new PgpVerifyResult
        {
            Valid = outcome.Valid,
            Detail = outcome.Detail,
            Signer = outcome.Signer is null ? null : ToKeyInfo(outcome.Signer),
        });

    private static PgpKeyInfoResult ToKeyInfo(PgpKeyInfo key) =>
        new()
        {
            Fingerprint = key.Fingerprint,
            KeyId = key.KeyId,
            UserId = key.UserId,
            Name = key.Name,
            Email = key.Email,
            Algorithm = key.Algorithm,
            Bits = key.Bits,
            CreatedAt = key.CreatedAt,
            ExpiresAt = key.ExpiresAt,
            HasPrivate = key.HasPrivate,
            CanEncrypt = key.CanEncrypt,
        };
}

public sealed class PgpGenerateKeyOperation : PgpOperation
{
    public PgpGenerateKeyOperation(IPgpService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.PgpGenerateKey;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out PgpGenerateKeyPayload payload))
        {
            throw MalformedPayload();
        }

        var request = new PgpGenerateKeyRequest(
            payload.Name ?? string.Empty,
            payload.Email ?? string.Empty,
            payload.Algorithm ?? string.Empty,
            payload.Bits,
            payload.ExpirationDays,
            payload.Passphrase);

        return ToKey(await Service.GenerateKeyAsync(request, cancellationToken).ConfigureAwait(false));
    }
}

public sealed class PgpListKeysOperation : PgpOperation
{
    public PgpListKeysOperation(IPgpService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.PgpListKeys;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken) =>
        // Payload cero (§6): se ignora lo que venga en él.
        ToKeyList(await Service.ListKeysAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class PgpImportKeyOperation : PgpOperation
{
    public PgpImportKeyOperation(IPgpService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.PgpImportKey;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out PgpImportKeyPayload payload))
        {
            throw MalformedPayload();
        }

        var request = new PgpImportKeyRequest(payload.ArmoredKey ?? string.Empty);
        return ToKey(await Service.ImportKeyAsync(request, cancellationToken).ConfigureAwait(false));
    }
}

public sealed class PgpExportKeyOperation : PgpOperation
{
    public PgpExportKeyOperation(IPgpService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.PgpExportKey;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out PgpExportKeyPayload payload))
        {
            throw MalformedPayload();
        }

        var request = new PgpExportKeyRequest(payload.Fingerprint ?? string.Empty, payload.IncludePrivate);
        var armored = await Service.ExportKeyAsync(request, cancellationToken).ConfigureAwait(false);

        // IsPrivate refleja lo pedido: la clave privada solo sale si se pidió.
        return BridgeJson.ToJson(new PgpExportKeyResult
        {
            ArmoredKey = armored,
            IsPrivate = payload.IncludePrivate,
        });
    }
}

public sealed class PgpEncryptOperation : PgpOperation
{
    public PgpEncryptOperation(IPgpService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.PgpEncrypt;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out PgpEncryptPayload payload))
        {
            throw MalformedPayload();
        }

        var request = new PgpEncryptRequest(
            payload.Text ?? string.Empty,
            payload.Recipients ?? Array.Empty<string>());

        var armored = await Service.EncryptAsync(request, cancellationToken).ConfigureAwait(false);
        return BridgeJson.ToJson(new PgpEncryptResult { ArmoredMessage = armored });
    }
}

public sealed class PgpDecryptOperation : PgpOperation
{
    public PgpDecryptOperation(IPgpService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.PgpDecrypt;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out PgpDecryptPayload payload))
        {
            throw MalformedPayload();
        }

        // La contraseña jamás se registra ni se refleja en los errores.
        var request = new PgpDecryptRequest(payload.ArmoredMessage ?? string.Empty, payload.Passphrase);
        var text = await Service.DecryptAsync(request, cancellationToken).ConfigureAwait(false);
        return BridgeJson.ToJson(new PgpDecryptResult { Text = text });
    }
}

public sealed class PgpSignOperation : PgpOperation
{
    public PgpSignOperation(IPgpService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.PgpSign;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out PgpSignPayload payload))
        {
            throw MalformedPayload();
        }

        var request = new PgpSignRequest(
            payload.Text ?? string.Empty,
            payload.Fingerprint ?? string.Empty,
            payload.Passphrase);

        var signature = await Service.SignAsync(request, cancellationToken).ConfigureAwait(false);
        return BridgeJson.ToJson(new PgpSignResult { Signature = signature });
    }
}

public sealed class PgpVerifyOperation : PgpOperation
{
    public PgpVerifyOperation(IPgpService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.PgpVerify;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out PgpVerifyPayload payload))
        {
            throw MalformedPayload();
        }

        var request = new PgpVerifyRequest(payload.Text ?? string.Empty, payload.Signature ?? string.Empty);
        var outcome = await Service.VerifyAsync(request, cancellationToken).ConfigureAwait(false);
        return ToVerify(outcome);
    }
}
