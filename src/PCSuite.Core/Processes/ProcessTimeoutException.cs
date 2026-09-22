namespace PCSuite.Core.Processes;

/// <summary>
/// El proceso superó el tiempo límite de la solicitud y fue terminado.
/// El mensaje es controlado (apto para la UI): nunca contiene stack traces.
/// </summary>
public sealed class ProcessTimeoutException : Exception
{
    public ProcessTimeoutException(TimeSpan timeout)
        : base($"El proceso superó el tiempo límite de {timeout.TotalSeconds:0.###} s y fue terminado.")
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
}
