using PCSuite.Contracts;
using PCSuite.Core.Jobs;
using PCSuite.Core.Operations;
using PCSuite.Core.Updates;
using PCSuite.Infrastructure.Operations;

namespace PCSuite.Infrastructure.Operations;

/// <summary>Resultado de update.check (payload cero → estado + error humano).</summary>
public sealed class UpdateCheckResultDto
{
    public bool Available { get; init; }

    public string CurrentVersion { get; init; } = "";

    public string LatestVersion { get; init; } = "";

    public long SizeBytes { get; init; }

    public string? ReleaseNotesUrl { get; init; }

    /// <summary>Null si todo fue bien; mensaje humano si falló (sin dramatismo).</summary>
    public string? Error { get; init; }

    /// <summary>
    /// Aviso TOFU (Task 17.6): la clave de firma del manifiesto CAMBIÓ desde
    /// la primera verificación. La UI debe mostrarlo prominentemente antes
    /// de dejar instalar; null = sin rotación.
    /// </summary>
    public string? KeyRotationWarning { get; init; }
}

/// <summary>Resultado de update.download: el job queda corriendo (job.event).</summary>
public sealed class UpdateDownloadResult
{
    public string JobId { get; init; } = "";
}

/// <summary>Resultado de update.install.</summary>
public sealed class UpdateInstallResult
{
    public bool Launching { get; init; }
}

/// <summary>Payload de update.install: { path } (la que devolvió el job).</summary>
public sealed class UpdateInstallPayload
{
    public string Path { get; init; } = "";
}

// =====================================================================
// Operaciones
// =====================================================================

/// <summary>update.check — consulta el manifiesto. NUNCA lanza (fallo = Available=false).</summary>
public sealed class UpdateCheckOperation(IPcsuiteUpdateService service) : IOperation
{
    public string Id => OperationIds.UpdateCheck;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        var check = await service.CheckAsync(ct).ConfigureAwait(false);

        var dto = new UpdateCheckResultDto
        {
            Available = check.Available,
            CurrentVersion = check.CurrentVersion,
            LatestVersion = check.LatestVersion,
            SizeBytes = check.SizeBytes,
            ReleaseNotesUrl = check.ReleaseNotesUrl,
            Error = check.Error,
            KeyRotationWarning = check.KeyRotationWarning,
        };
        return OperationResult.Ok(BridgeJson.ToJson(dto));
    }
}

/// <summary>
/// update.download — descarga el instalador como Job. Progreso 0..1 por
/// job.event (progress) y desenlace por statusChanged. El resultado del job
/// al completarse: { path }. SHA-256 en streaming: discrepancia → job
/// Failed con mensaje claro y archivo borrado.
/// </summary>
public sealed class UpdateDownloadOperation(
    IPcsuiteUpdateService service,
    IJobManager jobManager) : IOperation
{
    public string Id => OperationIds.UpdateDownload;
    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        var job = jobManager.Start("update.download");

        _ = Task.Run(async () =>
        {
            try
            {
                var path = await service.DownloadAsync(
                    (received, total) =>
                    {
                        if (total > 0)
                        {
                            jobManager.SetProgress(job.Id, (double)received / total);
                        }
                    },
                    jobManager.GetCancellationToken(job.Id)).ConfigureAwait(false);

                jobManager.Complete(job.Id, result: BridgeJson.ToJson(new { path }));
            }
            catch (OperationCanceledException)
            {
                jobManager.MarkCancelled(job.Id);
            }
            catch (Exception ex)
            {
                jobManager.Fail(job.Id, ex.Message);
            }
        }, CancellationToken.None);

        return Task.FromResult(OperationResult.Ok(
            BridgeJson.ToJson(new UpdateDownloadResult { JobId = job.Id })));
    }
}

/// <summary>
/// update.install — lanza el instalador YA verificado. Tras un lanzamiento
/// exitoso se dispara onInstalled (MainForm cierra la app para dejarle el
/// paso al Setup, que pide UAC él solo si toca).
/// </summary>
public sealed class UpdateInstallOperation(
    IPcsuiteUpdateService service,
    Action? onInstalled = null) : IOperation
{
    public string Id => OperationIds.UpdateInstall;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out UpdateInstallPayload payload)
            || payload is null)
        {
            return OperationResult.Fail("error", "El payload debe ser { path }.");
        }

        try
        {
            await service.InstallAsync(payload.Path, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail("install_failed", ex.Message);
        }

        // Lanzamiento exitoso: la app se cierra (la respuesta llega antes).
        onInstalled?.Invoke();
        return OperationResult.Ok(BridgeJson.ToJson(new UpdateInstallResult { Launching = true }));
    }
}
