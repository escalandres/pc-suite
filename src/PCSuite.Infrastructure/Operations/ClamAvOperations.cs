using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.ClamAV;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

// Task 008 (§13): las 7 operaciones clamav.* en un solo archivo (mismo
// patrón que WslOperations): payload estructurado SOLO en customScan
// ({"path"}, validado en el servicio) y payload cero en el resto; los argv
// fijos viven en ClamAvArguments y jamás salen del proceso .NET.

/// <summary>
/// Base de las operaciones clamav.* del puente: traducción de
/// <see cref="ClamAvException"/> a errores controlados (código + mensaje
/// aptos para la UI, nunca stack traces) y mapeo dominio → DTO camelCase.
/// </summary>
public abstract class ClamAvOperation : IOperation
{
    protected ClamAvOperation(IClamAvService service) =>
        Service = service ?? throw new ArgumentNullException(nameof(service));

    protected IClamAvService Service { get; }

    public abstract string Id { get; }

    /// <summary>
    /// Solo clamav.install (vía winget) y clamav.updateDefinitions (freshclam
    /// en Program Files) pueden terminar en un proceso elevado; la decisión
    /// real es runtime dentro de los servicios (nunca UAC preventivo) y los
    /// escaneos jamás elevan.
    /// </summary>
    public virtual bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        try
        {
            var data = await ExecuteCoreAsync(context, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(data);
        }
        catch (ClamAvException exception)
        {
            return OperationResult.Fail(exception.ErrorCode, exception.ErrorMessage);
        }
    }

    protected abstract Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken);

    protected static JsonElement? ToStatus(ClamAvStatus status) =>
        BridgeJson.ToJson(new ClamAvStatusResult
        {
            Installed = status.Installed,
            Path = status.Path,
            Version = status.Version,
            Definitions = status.Definitions,
            CanUpdateDefinitions = status.CanUpdateDefinitions,
        });

    protected static JsonElement? ToMutation(ClamAvMutationOutcome outcome) =>
        BridgeJson.ToJson(new ClamAvMutationResult
        {
            Succeeded = outcome.Succeeded,
            ExitCode = outcome.ExitCode,
            Elevated = outcome.Elevated,
            Detail = outcome.Detail,
        });

    protected static JsonElement? ToScan(ClamAvScanOutcome outcome) =>
        BridgeJson.ToJson(new ClamAvScanResult
        {
            Succeeded = outcome.Succeeded,
            ExitCode = outcome.ExitCode,
            ScannedFiles = outcome.ScannedFiles,
            ScannedDirectories = outcome.ScannedDirectories,
            InfectedFiles = outcome.InfectedFiles,
            EngineVersion = outcome.EngineVersion,
            KnownViruses = outcome.KnownViruses,
            Detail = outcome.Detail,
            Threats = outcome.Threats
                .Select(threat => new ClamAvThreatResult { File = threat.File, Threat = threat.Threat })
                .ToList(),
        });

    protected static JsonElement? ToHistory(IReadOnlyList<ClamAvScanHistoryEntry> entries) =>
        BridgeJson.ToJson(new ClamAvHistoryResult
        {
            Entries = entries
                .Select(entry => new ClamAvHistoryEntryResult
                {
                    Timestamp = entry.Timestamp,
                    Kind = entry.Kind,
                    Path = entry.Path,
                    Succeeded = entry.Succeeded,
                    ScannedFiles = entry.ScannedFiles,
                    InfectedFiles = entry.InfectedFiles,
                    Detail = entry.Detail,
                    Threats = entry.Threats
                        .Select(threat => new ClamAvThreatResult { File = threat.File, Threat = threat.Threat })
                        .ToList(),
                })
                .ToList(),
        });
}

public sealed class ClamAvStatusOperation : ClamAvOperation
{
    public ClamAvStatusOperation(IClamAvService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.ClamavStatus;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken) =>
        ToStatus(await Service.GetStatusAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class ClamAvInstallOperation : ClamAvOperation
{
    public ClamAvInstallOperation(IClamAvService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.ClamavInstall;

    public override bool RequiresElevation => true;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken) =>
        ToMutation(await Service.InstallAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class ClamAvUpdateDefinitionsOperation : ClamAvOperation
{
    public ClamAvUpdateDefinitionsOperation(IClamAvService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.ClamavUpdateDefinitions;

    public override bool RequiresElevation => true;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken) =>
        ToMutation(await Service.UpdateDefinitionsAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class ClamAvQuickScanOperation : ClamAvOperation
{
    public ClamAvQuickScanOperation(IClamAvService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.ClamavQuickScan;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken) =>
        ToScan(await Service.QuickScanAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class ClamAvFullScanOperation : ClamAvOperation
{
    public ClamAvFullScanOperation(IClamAvService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.ClamavFullScan;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken) =>
        ToScan(await Service.FullScanAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class ClamAvCustomScanOperation : ClamAvOperation
{
    public ClamAvCustomScanOperation(IClamAvService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.ClamavCustomScan;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out ClamAvScanPayload payload))
        {
            throw new ClamAvException(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON.");
        }

        // payload.Path null/vacío o ruta inválida → ClamAvException controlada
        // desde el servicio (validación de raíz, existencia y sin NUL).
        return ToScan(await Service.CustomScanAsync(payload.Path ?? string.Empty, cancellationToken).ConfigureAwait(false));
    }
}

public sealed class ClamAvHistoryOperation : ClamAvOperation
{
    public ClamAvHistoryOperation(IClamAvService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.ClamavHistory;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken) =>
        ToHistory(await Service.GetHistoryAsync(cancellationToken).ConfigureAwait(false));
}
