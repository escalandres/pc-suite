using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.AI;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.AI;

namespace PCSuite.Infrastructure.Operations;

// Task 038: las 3 operaciones ai.* del puente (mismo patrón que ClamAvOperations):
// payload estructurado SOLO en ai.convert ({"path"}, validado en el servicio) y
// payload cero en el resto; los argv fijos viven en AiArguments y jamás salen
// del proceso .NET.

/// <summary>
/// Base de las operaciones ai.* del puente: traducción de
/// <see cref="AiException"/> a errores controlados (código + mensaje aptos para
/// la UI, nunca stack traces) y mapeo dominio → DTO camelCase.
/// </summary>
public abstract class AiOperation : IOperation
{
    protected AiOperation(IAiService service) =>
        Service = service ?? throw new ArgumentNullException(nameof(service));

    protected IAiService Service { get; }

    public abstract string Id { get; }

    public virtual bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        try
        {
            var data = await ExecuteCoreAsync(context, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(data);
        }
        catch (AiException exception)
        {
            return OperationResult.Fail(exception.ErrorCode, exception.ErrorMessage);
        }
    }

    protected abstract Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken);

    protected static JsonElement? ToStatus(AiStatus status) =>
        BridgeJson.ToJson(new AiStatusResult
        {
            Installed = status.Installed,
            PythonPath = status.PythonPath,
            MarkItDownVersion = status.MarkItDownVersion,
            Detail = status.Detail,
        });

    protected static JsonElement? ToMutation(AiMutationOutcome outcome) =>
        BridgeJson.ToJson(new AiMutationResult
        {
            Succeeded = outcome.Succeeded,
            ExitCode = outcome.ExitCode,
            Elevated = outcome.Elevated,
            Detail = outcome.Detail,
        });

    protected static JsonElement? ToConvert(AiConvertOutcome outcome) =>
        BridgeJson.ToJson(new AiConvertResult
        {
            Succeeded = outcome.Succeeded,
            ExitCode = outcome.ExitCode,
            OutputPath = outcome.OutputPath,
            Detail = outcome.Detail,
        });

    protected static JsonElement? ToOllamaStatus(OllamaStatus status) =>
        BridgeJson.ToJson(new OllamaStatusResult
        {
            Installed = status.Installed,
            OllamaPath = status.OllamaPath,
            Version = status.Version,
            Detail = status.Detail,
        });

    protected static JsonElement? ToOllamaModels(IReadOnlyList<OllamaModel> models) =>
        BridgeJson.ToJson(new OllamaModelCollectionResult
        {
            Models = models
                .Select(m => new OllamaModelResult
                {
                    Name = m.Name,
                    Size = m.Size,
                    Modified = m.Modified,
                    Digest = m.Digest,
                })
                .ToList(),
        });
}

public sealed class AiDetectOperation : AiOperation
{
    public AiDetectOperation(IAiService service)
        : base(service) { }

    public override string Id => OperationIds.AiDetect;

    protected override async Task<JsonElement?> ExecuteCoreAsync(
        OperationContext context, CancellationToken cancellationToken) =>
        ToStatus(await Service.GetStatusAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class AiInstallOperation : AiOperation
{
    public AiInstallOperation(IAiService service)
        : base(service) { }

    public override string Id => OperationIds.AiInstall;

    /// <summary>La instalación puede requerir UAC si el entorno de Python lo pide.</summary>
    public override bool RequiresElevation => true;

    protected override async Task<JsonElement?> ExecuteCoreAsync(
        OperationContext context, CancellationToken cancellationToken) =>
        ToMutation(await Service.InstallAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class AiConvertOperation : AiOperation
{
    public AiConvertOperation(IAiService service)
        : base(service) { }

    public override string Id => OperationIds.AiConvert;

    protected override async Task<JsonElement?> ExecuteCoreAsync(
        OperationContext context, CancellationToken cancellationToken)
    {
        // Payload no-objeto → MALFORMED_MESSAGE (convención del puente).
        if (context.Payload is not null
            && context.Payload.Value.ValueKind != JsonValueKind.Object
            && context.Payload.Value.ValueKind != JsonValueKind.Null)
        {
            throw new AiException(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON.");
        }

        if (!ToolMapper.TryParsePayload(context.Payload, out AiConvertPayload payload))
        {
            throw new AiException(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON.");
        }

        // Validación INMEDIATA: ruta absoluta, existente, sin NUL.
        var path = payload.Path ?? string.Empty;
        Service.ValidateConvertPath(path);

        var outcome = await Service.ConvertAsync(path, cancellationToken).ConfigureAwait(false);
        return ToConvert(outcome);
    }
}

public sealed class OllamaDetectOperation : AiOperation
{
    public OllamaDetectOperation(IAiService service)
        : base(service) { }

    public override string Id => OperationIds.OllamaDetect;

    protected override async Task<JsonElement?> ExecuteCoreAsync(
        OperationContext context, CancellationToken cancellationToken) =>
        ToOllamaStatus(await Service.GetOllamaStatusAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class OllamaListModelsOperation : AiOperation
{
    public OllamaListModelsOperation(IAiService service)
        : base(service) { }

    public override string Id => OperationIds.OllamaListModels;

    protected override async Task<JsonElement?> ExecuteCoreAsync(
        OperationContext context, CancellationToken cancellationToken) =>
        ToOllamaModels(await Service.ListOllamaModelsAsync(cancellationToken).ConfigureAwait(false));
}
