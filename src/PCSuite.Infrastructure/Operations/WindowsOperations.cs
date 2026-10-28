using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Jobs;
using PCSuite.Core.Operations;
using PCSuite.Core.Windows;
using PCSuite.Infrastructure.Windows;

namespace PCSuite.Infrastructure.Operations;

// Task 036: las 2 operaciones windows.* del puente (mismo patrón que
// ClamAvOperations): listCommands es directa; runCommand es un JOB que
// responde { jobId } en el acto (sfc/DISM pueden tardar horas) con el
// desenlace como Result del job. El payload lleva SOLO el id del catálogo
// — jamás un comando libre: argv fijo en WindowsCommandCatalog, UAC por
// operación y cero helper elevado nuevo (runas directo como los escaneos
// de ClamAV).

public sealed class WindowsListCommandsOperation : IOperation
{
    private readonly IWindowsRepairService _service;

    public WindowsListCommandsOperation(IWindowsRepairService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));

    public string Id => OperationIds.WindowsListCommands;

    /// <summary>Nunca: el catálogo es de solo lectura.</summary>
    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        var dto = new WindowsCommandCollectionResult
        {
            Commands = _service
                .List()
                .Select(command => new WindowsCommandInfoResult
                {
                    Id = command.Id,
                    Name = command.Name,
                    Description = command.Description,
                    Category = command.Category,
                    DisplayCommand = command.DisplayCommand,
                    DurationHint = command.DurationHint,
                    Heavy = command.Heavy,
                    LogPath = command.LogPath,
                })
                .ToList(),
        };

        return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(dto)));
    }
}

/// <summary>
/// windows.runCommand como JOB: valida el id ANTES de crear nada (un id
/// desconocido responde con error controlado y sin job residual), arranca
/// el proceso elevado y el desenlace viaja en el Result del job.
/// </summary>
public sealed class WindowsRunCommandOperation : IOperation
{
    /// <summary>Tipo de job para el historial (no hay re-adjunta v1: un comando a la vez).</summary>
    public const string JobType = "windows.repair";

    private readonly IWindowsRepairService _service;
    private readonly IJobManager _jobs;

    public WindowsRunCommandOperation(IWindowsRepairService service, IJobManager jobs)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Id => OperationIds.WindowsRunCommand;

    /// <summary>Nunca a nivel de op: la elevación la genera Windows por proceso (runas).</summary>
    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        // Payload no-objeto → MALFORMED_MESSAGE (convención del puente).
        if (context.Payload is not null
            && context.Payload.Value.ValueKind != JsonValueKind.Object
            && context.Payload.Value.ValueKind != JsonValueKind.Null)
        {
            return Task.FromResult(OperationResult.Fail(
                BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON."));
        }

        if (!ToolMapper.TryParsePayload(context.Payload, out WindowsRunPayload payload))
        {
            return Task.FromResult(OperationResult.Fail(
                BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON."));
        }

        // Allow-list ANTES del job y ANTES del UAC: id desconocido = error
        // controlado, sin job residual ni prompt.
        var commandId = payload.CommandId ?? string.Empty;
        if (WindowsCommandCatalog.Find(commandId) is null)
        {
            return Task.FromResult(OperationResult.Fail(
                BridgeErrorCodes.WindowsFailed, $"Comando desconocido: '{commandId}'."));
        }

        var job = _jobs.Start(JobType);
        var token = _jobs.GetCancellationToken(job.Id);

        // Fire-and-forget con desenlace SIEMPRE controlado (patrón escaneos).
        _ = Task.Run(() => RunJobAsync(job.Id, token, commandId), CancellationToken.None);

        return Task.FromResult(
            OperationResult.Ok(BridgeJson.ToJson(new WindowsRunStartResult { JobId = job.Id })));
    }

    private async Task RunJobAsync(string jobId, CancellationToken token, string commandId)
    {
        try
        {
            var outcome = await _service.RunAsync(commandId, token).ConfigureAwait(false);

            _jobs.Complete(
                jobId,
                outcome.ExitCode,
                BridgeJson.ToJson(new WindowsRunOutcomeResult
                {
                    Succeeded = outcome.Succeeded,
                    ExitCode = outcome.ExitCode,
                    Message = outcome.Message,
                    LogHint = outcome.LogHint,
                }));
        }
        catch (WindowsRepairException exception)
        {
            _jobs.Fail(jobId, exception.Message);
        }
        catch (OperationCanceledException)
        {
            _jobs.MarkCancelled(jobId);
        }
        catch (Exception)
        {
            _jobs.Fail(jobId, "El comando falló de forma inesperada.");
        }
    }
}
