using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Cleaner;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

// Task 011 (parte 11.3): las 2 operaciones cleaner.* en un solo archivo
// (mismo patrón que WslOperations/ClamAvOperations): payload cero en scan y
// payload estructurado {"categories":[ids]} en clean, SIEMPRE ids de la lista
// fija — jamás rutas — y CleanerException traducida a error controlado
// (código + mensaje apto para la UI, nunca stack traces).

/// <summary>Base de las operaciones cleaner.* del puente.</summary>
public abstract class CleanerOperation : IOperation
{
    protected CleanerOperation(ICleanerService service) =>
        Service = service ?? throw new ArgumentNullException(nameof(service));

    protected ICleanerService Service { get; }

    public abstract string Id { get; }

    /// <summary>
    /// cleaner.* jamás eleva: sin administrador, lo que la ACL no permite se
    /// omite y se contabiliza en el resultado (nunca UAC preventivo).
    /// </summary>
    public virtual bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        try
        {
            var data = await ExecuteCoreAsync(context, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(data);
        }
        catch (CleanerException exception)
        {
            return OperationResult.Fail(exception.ErrorCode, exception.ErrorMessage);
        }
    }

    protected abstract Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken);

    protected static JsonElement? ToScan(CleanerScanOutcome outcome) =>
        BridgeJson.ToJson(new CleanerScanResult
        {
            Categories = outcome.Categories
                .Select(category => new CleanerCategoryScanResult
                {
                    Id = category.Id,
                    Label = category.Label,
                    Path = category.Path,
                    FileCount = category.FileCount,
                    SizeBytes = category.SizeBytes,
                    Accessible = category.Accessible,
                })
                .ToList(),
        });

    protected static JsonElement? ToClean(CleanerCleanOutcome outcome) =>
        BridgeJson.ToJson(new CleanerCleanResult
        {
            Categories = outcome.Categories
                .Select(category => new CleanerCategoryCleanResult
                {
                    Id = category.Id,
                    DeletedFiles = category.DeletedFiles,
                    FreedBytes = category.FreedBytes,
                    SkippedFiles = category.SkippedFiles,
                    Detail = category.Detail,
                })
                .ToList(),
        });
}

public sealed class CleanerScanOperation : CleanerOperation
{
    public CleanerScanOperation(ICleanerService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.CleanerScan;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken) =>
        ToScan(await Service.ScanAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class CleanerCleanOperation : CleanerOperation
{
    public CleanerCleanOperation(ICleanerService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.CleanerClean;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out CleanerCleanPayload payload))
        {
            throw new CleanerException(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON.");
        }

        // Categories null (payload ausente o sin el campo) o ids desconocidos →
        // CleanerException controlada desde el servicio, sin eco de la entrada.
        return ToClean(await Service.CleanAsync(payload.Categories!, cancellationToken).ConfigureAwait(false));
    }
}
