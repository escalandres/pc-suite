using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Core.Jobs;

namespace PCSuite.Application.Jobs;

/// <summary>
/// Publicador in-memory de eventos de jobs. Un suscriptor que lance una
/// excepción no interrumpe el motor: se registra y se sigue adelante.
/// </summary>
public sealed class JobEventPublisher : IJobEventPublisher
{
    private readonly ILogger<JobEventPublisher> _logger;

    public JobEventPublisher(ILogger<JobEventPublisher>? logger = null)
    {
        _logger = logger ?? NullLogger<JobEventPublisher>.Instance;
    }

    public event EventHandler<JobEvent>? Published;

    public void Publish(JobEvent jobEvent)
    {
        ArgumentNullException.ThrowIfNull(jobEvent);

        var handlers = Published;
        if (handlers is null)
        {
            return;
        }

        try
        {
            handlers(this, jobEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Suscriptor de eventos de job lanzó una excepción para {JobId} ({Type}).", jobEvent.JobId, jobEvent.Type);
        }
    }
}
