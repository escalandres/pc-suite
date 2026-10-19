using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.ClamAV;
using PCSuite.Core.Jobs;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.ClamAV;

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

/// <summary>
/// Base de los escaneos clamav.* como JOBS: la respuesta es {"jobId"} EN EL
/// ACTO y el desenlace llega por evento job.event (o sondeo job.get) con el
/// resultado del escaneo como Result del job. Cancelación vía job.cancel.
/// La promesa del puente ya no queda retenida durante un escaneo de horas.
/// </summary>
public abstract class ClamAvScanJobOperation : ClamAvOperation
{
    private readonly IJobManager _jobs;

    protected ClamAvScanJobOperation(IClamAvService service, IJobManager jobs)
        : base(service) => _jobs = jobs;

    protected abstract string JobType { get; }

    /// <summary>
    /// Valida el payload y devuelve la función que arranca el escaneo con el
    /// token del job y su progreso. Se llama ANTES de crear el job: un
    /// payload inválido responde con el error controlado sin job residual.
    /// </summary>
    protected abstract Func<CancellationToken, IProgress<ClamAvScanProgress>?, Task<ClamAvScanOutcome>> PrepareScan(
        OperationContext context);

    protected sealed override Task<JsonElement?> ExecuteCoreAsync(
        OperationContext context, CancellationToken cancellationToken)
    {
        var scan = PrepareScan(context);
        var job = _jobs.Start(JobType);
        var token = _jobs.GetCancellationToken(job.Id);

        // Fire-and-forget con desenlace SIEMPRE controlado (nunca lanza ni
        // deja excepciones sin observar): el ciclo de vida del job refleja
        // el resultado del escaneo.
        _ = Task.Run(() => RunScanJobAsync(job.Id, token, scan));

        return Task.FromResult(BridgeJson.ToJson(new ClamAvScanStartResult { JobId = job.Id }));
    }

    private async Task RunScanJobAsync(
        string jobId,
        CancellationToken token,
        Func<CancellationToken, IProgress<ClamAvScanProgress>?, Task<ClamAvScanOutcome>> scan)
    {
        try
        {
            // El progreso 0..1 alimenta el job (throttled por el reporter);
            // el evento clamav.scan.progress sigue llevando el detalle vivo.
            var progress = new Progress<ClamAvScanProgress>(p =>
                _jobs.SetProgress(jobId, (p.Percent ?? 0) / 100d));

            var outcome = await scan(token, progress).ConfigureAwait(false);

            // El outcome viaja completo como Result del job (evento + job.get).
            _jobs.Complete(jobId, outcome.ExitCode, ToScan(outcome));
        }
        catch (ClamAvException exception)
        {
            _jobs.Fail(jobId, exception.ErrorMessage);
        }
        catch (OperationCanceledException)
        {
            _jobs.MarkCancelled(jobId);
        }
        catch (Exception)
        {
            // Fallo inesperado: mensaje controlado, sin stack hacia la UI.
            _jobs.Fail(jobId, "El escaneo falló de forma inesperada.");
        }
    }
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

/// <summary>Resultado de clamav.quickTargets: las carpetas del escaneo rápido.</summary>
public sealed class ClamAvQuickTargetsResult
{
    public List<string> Targets { get; init; } = [];
}

/// <summary>
/// clamav.quickTargets — carpetas que escaneará el escaneo rápido, resueltas
/// y existentes. Solo lectura y sin elevación: la UI la usa para mostrar el
/// EXACTO alcance del escaneo en un modal ANTES de pedir el UAC.
/// </summary>
public sealed class ClamAvQuickTargetsOperation : IOperation
{
    public string Id => OperationIds.ClamavQuickTargets;

    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(new ClamAvQuickTargetsResult
        {
            Targets = [.. ClamAvArguments.QuickScanTargets()],
        })));
}

public sealed class ClamAvQuickScanOperation : ClamAvScanJobOperation
{
    public ClamAvQuickScanOperation(IClamAvService service, IJobManager jobs)
        : base(service, jobs)
    {
    }

    public override string Id => OperationIds.ClamavQuickScan;

    protected override string JobType => "clamav.quickScan";

    protected override Func<CancellationToken, IProgress<ClamAvScanProgress>?, Task<ClamAvScanOutcome>> PrepareScan(
        OperationContext context) =>
        (token, progress) => Service.QuickScanAsync(token, progress);
}

public sealed class ClamAvFullScanOperation : ClamAvScanJobOperation
{
    public ClamAvFullScanOperation(IClamAvService service, IJobManager jobs)
        : base(service, jobs)
    {
    }

    public override string Id => OperationIds.ClamavFullScan;

    protected override string JobType => "clamav.fullScan";

    protected override Func<CancellationToken, IProgress<ClamAvScanProgress>?, Task<ClamAvScanOutcome>> PrepareScan(
        OperationContext context) =>
        (token, progress) => Service.FullScanAsync(token, progress);
}

public sealed class ClamAvCustomScanOperation : ClamAvScanJobOperation
{
    public ClamAvCustomScanOperation(IClamAvService service, IJobManager jobs)
        : base(service, jobs)
    {
    }

    public override string Id => OperationIds.ClamavCustomScan;

    protected override string JobType => "clamav.customScan";

    protected override Func<CancellationToken, IProgress<ClamAvScanProgress>?, Task<ClamAvScanOutcome>> PrepareScan(
        OperationContext context)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out ClamAvScanPayload payload))
        {
            throw new ClamAvException(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON.");
        }

        // Validación INMEDIATA (antes de crear el job): payload.Path
        // null/vacío o ruta inválida → ClamAvException controlada desde el
        // servicio (raíz absoluta, existente y sin NUL).
        var path = payload.Path ?? string.Empty;
        Service.ValidateScanPath(path);
        return (token, progress) => Service.CustomScanAsync(path, token, progress);
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
