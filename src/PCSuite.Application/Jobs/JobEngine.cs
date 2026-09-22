using PCSuite.Core.Jobs;
using PCSuite.Core.Processes;

namespace PCSuite.Application.Jobs;

/// <summary>
/// Ejecuta procesos como jobs: registra el ciclo de vida, reenvía las líneas
/// de stdout/stderr como eventos en streaming y traduce cada desenlace a un
/// estado de job (Completed con exit 0, Failed con mensaje controlado,
/// Cancelled cuando se pide cancelación).
///
/// Contrato de cancelación: si cancela el token del llamador se lanza
/// <see cref="OperationCanceledException"/>; si se cancela el propio job
/// (vía <c>IJobManager.RequestCancel</c>) o expira el timeout, el job se
/// cierra y se devuelve su snapshot sin lanzar nada.
/// </summary>
public sealed class JobEngine
{
    private readonly IProcessRunner _runner;
    private readonly IJobManager _jobs;
    private readonly IJobEventPublisher _events;

    public JobEngine(IProcessRunner runner, IJobManager jobs, IJobEventPublisher events)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public async Task<Job> RunAsync(string type, ProcessRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var job = _jobs.Start(type);
        var jobCancellation = _jobs.GetCancellationToken(job.Id);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, jobCancellation);

        var streamedRequest = request with
        {
            OnStandardOutput = line => PublishLine(job.Id, JobLogStream.StandardOutput, line),
            OnStandardError = line => PublishLine(job.Id, JobLogStream.StandardError, line),
        };

        try
        {
            var result = await _runner.ExecuteAsync(streamedRequest, linked.Token).ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                _jobs.Complete(job.Id, result.ExitCode);
            }
            else
            {
                _jobs.Fail(job.Id, $"El proceso terminó con el código {result.ExitCode}.", result.ExitCode);
            }
        }
        catch (ProcessTimeoutException ex)
        {
            _jobs.Fail(job.Id, ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _jobs.MarkCancelled(job.Id);
            throw;
        }
        catch (OperationCanceledException)
        {
            _jobs.MarkCancelled(job.Id);
        }
        catch (Exception ex)
        {
            // Solo el mensaje: los detalles técnicos nunca llegan a la UI.
            _jobs.Fail(job.Id, ex.Message);
        }

        return _jobs.Get(job.Id)!;
    }

    private void PublishLine(string jobId, JobLogStream stream, string line)
        => _events.Publish(new JobEvent { JobId = jobId, Type = JobEventType.Output, Stream = stream, Line = line });
}
