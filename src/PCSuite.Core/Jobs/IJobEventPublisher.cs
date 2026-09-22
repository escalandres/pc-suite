namespace PCSuite.Core.Jobs;

/// <summary>
/// Publicación de eventos de jobs a los suscriptores. El publicador nunca
/// lanza: un suscriptor defectuoso no debe tumbar el motor.
/// </summary>
public interface IJobEventPublisher
{
    event EventHandler<JobEvent>? Published;

    void Publish(JobEvent jobEvent);
}
