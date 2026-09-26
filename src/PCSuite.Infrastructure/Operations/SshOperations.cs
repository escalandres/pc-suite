using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.SSH;

namespace PCSuite.Infrastructure.Operations;

// Task 009 (§5.1–5.2): las 6 operaciones ssh.* en un solo archivo (mismo
// patrón que ClamAvOperations): payloads estructurados SOLO en list/generate/
// inspect/importPublicKey (validados en el servicio) y payload ignorado en
// detect/openDirectory; la generación invoca ssh-keygen con argv fijo y el
// resto (listado, inspección, fingerprint, importación) es parseo .NET puro.

/// <summary>
/// Base de las operaciones ssh.* del puente: traducción de
/// <see cref="SshException"/> a errores controlados (código + mensaje aptos
/// para la UI, nunca stack traces) y mapeo dominio → DTO camelCase.
/// </summary>
public abstract class SshOperation : IOperation
{
    protected SshOperation(ISshKeyService service) =>
        Service = service ?? throw new ArgumentNullException(nameof(service));

    protected ISshKeyService Service { get; }

    public abstract string Id { get; }

    /// <summary>
    /// Task 009 jamás eleva: ssh-keygen corre sin privilegios y abrir el
    /// Explorador es una interacción de usuario, no una operación administrativa.
    /// </summary>
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        try
        {
            var data = await ExecuteCoreAsync(context, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(data);
        }
        catch (SshException exception)
        {
            return OperationResult.Fail(exception.ErrorCode, exception.ErrorMessage);
        }
    }

    protected abstract Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken);

    /// <summary>Payload no-objeto → MALFORMED_MESSAGE (mismo texto que clamav.customScan).</summary>
    protected static SshException MalformedPayload() =>
        new(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON.");

    protected static JsonElement? ToDetect(SshDetectStatus status) =>
        BridgeJson.ToJson(new SshDetectResult { Available = status.Available, Path = status.Path });

    protected static JsonElement? ToKeyList(IReadOnlyList<SshKeyInfo> keys) =>
        BridgeJson.ToJson(new SshKeyListResult { Keys = keys.Select(ToKeyInfo).ToArray() });

    protected static JsonElement? ToKey(SshKeyInfo key) => BridgeJson.ToJson(ToKeyInfo(key));

    protected static JsonElement? ToGenerate(SshGenerateOutcome outcome) =>
        BridgeJson.ToJson(new SshGenerateResult
        {
            Succeeded = outcome.Succeeded,
            Detail = outcome.Detail,
            PrivateKeyPath = outcome.PrivateKeyPath,
            PublicKeyPath = outcome.PublicKeyPath,
            Key = outcome.Key is null ? null : ToKeyInfo(outcome.Key),
        });

    protected static JsonElement? ToImport(SshImportOutcome outcome) =>
        BridgeJson.ToJson(new SshImportPublicKeyResult
        {
            Path = outcome.Path,
            Key = ToKeyInfo(outcome.Key),
        });

    protected static JsonElement? ToDirectory(string directory) =>
        BridgeJson.ToJson(new SshOpenDirectoryResult { Path = directory });

    private static SshKeyInfoResult ToKeyInfo(SshKeyInfo key) =>
        new()
        {
            Type = key.Type,
            Algorithm = key.Algorithm,
            Bits = key.Bits,
            Fingerprint = key.Fingerprint,
            Comment = key.Comment,
            Format = key.Format,
            Path = key.Path,
            HasPrivate = key.HasPrivate,
            PublicKey = key.PublicKey,
        };
}

public sealed class SshDetectOperation : SshOperation
{
    public SshDetectOperation(ISshKeyService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.SshDetect;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken) =>
        ToDetect(await Service.DetectAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class SshListOperation : SshOperation
{
    public SshListOperation(ISshKeyService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.SshList;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out SshListPayload payload))
        {
            throw MalformedPayload();
        }

        // Directory null/vacío → ~/.ssh; ruta no absoluta → SshException controlada.
        return ToKeyList(await Service.ListAsync(payload.Directory, cancellationToken).ConfigureAwait(false));
    }
}

public sealed class SshGenerateOperation : SshOperation
{
    public SshGenerateOperation(ISshKeyService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.SshGenerate;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out SshGeneratePayload payload))
        {
            throw MalformedPayload();
        }

        // Campos extra del payload (args/command/…) se descartan al
        // deserializar: el servicio valida y construye el argv fijo.
        var request = new SshGenerateRequest(
            payload.Algorithm ?? string.Empty,
            payload.Bits,
            payload.Comment ?? string.Empty,
            payload.Directory ?? string.Empty,
            payload.Passphrase);

        return ToGenerate(await Service.GenerateAsync(request, cancellationToken).ConfigureAwait(false));
    }
}

public sealed class SshInspectOperation : SshOperation
{
    public SshInspectOperation(ISshKeyService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.SshInspect;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out SshInspectPayload payload))
        {
            throw MalformedPayload();
        }

        return ToKey(await Service.InspectAsync(payload.Path ?? string.Empty, cancellationToken).ConfigureAwait(false));
    }
}

public sealed class SshImportPublicKeyOperation : SshOperation
{
    public SshImportPublicKeyOperation(ISshKeyService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.SshImportPublicKey;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out SshImportPublicKeyPayload payload))
        {
            throw MalformedPayload();
        }

        var request = new SshImportRequest(
            payload.Key ?? string.Empty,
            payload.FileName ?? string.Empty,
            payload.Directory ?? string.Empty);

        return ToImport(await Service.ImportPublicKeyAsync(request, cancellationToken).ConfigureAwait(false));
    }
}

public sealed class SshOpenDirectoryOperation : SshOperation
{
    public SshOpenDirectoryOperation(ISshKeyService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.SshOpenDirectory;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken) =>
        ToDirectory(await Service.OpenSshDirectoryAsync(cancellationToken).ConfigureAwait(false));
}
