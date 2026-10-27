using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Files;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

// Task 033: las 4 operaciones files.* del puente (mismo patrón que
// PgpOperations): payloads estructurados camelCase validados en el
// servicio, FileSecurityException/IOException/UnauthorizedAccessException →
// error controlado FILES_FAILED apto para la UI (nunca stack traces) y
// cero elevación — todo es .NET puro sobre archivos locales. Sin ILogger
// a propósito: contraseñas y rutas con datos del usuario jamás se registran.
//
// files.stegoEmbed / files.stegoExtract (Task 033.2) NO se registran: la
// esteganografía sigue sin implementarse y no hay UI que las invoque;
// registrar una op que solo puede fallar agranda la superficie del puente
// sin aportar nada. Cuando 033.2 aterrice, se suman aquí.

/// <summary>
/// Base de las operaciones files.*: traducción de los fallos del servicio a
/// errores controlados (código + mensaje aptos para la UI, sin stack traces).
/// </summary>
public abstract class FileSecurityOperation : IOperation
{
    protected FileSecurityOperation(IFileSecurityService service) =>
        Service = service ?? throw new ArgumentNullException(nameof(service));

    protected IFileSecurityService Service { get; }

    public abstract string Id { get; }

    /// <summary>Task 033 jamás eleva: crypto .NET en el proceso actual.</summary>
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
        catch (FileSecurityException exception)
        {
            return OperationResult.Fail(BridgeErrorCodes.FilesFailed, exception.Message);
        }
        catch (IOException)
        {
            return OperationResult.Fail(BridgeErrorCodes.FilesFailed, "Error de disco al procesar el archivo.");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResult.Fail(BridgeErrorCodes.FilesFailed, "No hay permisos suficientes sobre el archivo.");
        }
    }

    protected abstract Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken);

    /// <summary>Payload no-objeto → MALFORMED_MESSAGE (mismo texto que pgp.*).</summary>
    protected static FileSecurityException MalformedPayload() =>
        new("El payload debe ser un objeto JSON.");
}

public sealed class FilesEncryptOperation : FileSecurityOperation
{
    public FilesEncryptOperation(IFileSecurityService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.FilesEncrypt;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out FilesEncryptPayload payload))
        {
            throw MalformedPayload();
        }

        var result = await Service.EncryptAsync(
            new FileEncryptRequest(
                payload.InputPath ?? string.Empty,
                payload.Password ?? string.Empty,
                payload.OutputPath,
                payload.DeleteOriginal),
            cancellationToken).ConfigureAwait(false);

        return BridgeJson.ToJson(result);
    }
}

public sealed class FilesDecryptOperation : FileSecurityOperation
{
    public FilesDecryptOperation(IFileSecurityService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.FilesDecrypt;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out FilesDecryptPayload payload))
        {
            throw MalformedPayload();
        }

        var result = await Service.DecryptAsync(
            new FileDecryptRequest(
                payload.InputPath ?? string.Empty,
                payload.Password ?? string.Empty,
                payload.OutputPath),
            cancellationToken).ConfigureAwait(false);

        return BridgeJson.ToJson(result);
    }
}

public sealed class FilesShredOperation : FileSecurityOperation
{
    public FilesShredOperation(IFileSecurityService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.FilesShred;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out FilesShredPayload payload))
        {
            throw MalformedPayload();
        }

        var result = await Service.ShredAsync(
            new FileShredRequest(payload.Path ?? string.Empty, payload.Passes),
            cancellationToken).ConfigureAwait(false);

        return BridgeJson.ToJson(result);
    }
}

public sealed class FilesHashOperation : FileSecurityOperation
{
    public FilesHashOperation(IFileSecurityService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.FilesHash;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out FilesHashPayload payload))
        {
            throw MalformedPayload();
        }

        var result = await Service.HashAsync(payload.Path ?? string.Empty, cancellationToken).ConfigureAwait(false);
        return BridgeJson.ToJson(result);
    }
}
