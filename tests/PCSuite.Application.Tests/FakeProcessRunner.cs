using PCSuite.Core.Processes;

namespace PCSuite.Application.Tests;

/// <summary>
/// <see cref="IProcessRunner"/> en memoria: registra las solicitudes recibidas
/// y responde según el manejador preprogramado, sin ejecutar nada real.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<ProcessRequest, CancellationToken, Task<ProcessResult>> _handler;

    public FakeProcessRunner(Func<ProcessRequest, CancellationToken, Task<ProcessResult>> handler)
    {
        _handler = handler;
    }

    /// <summary>Solicitudes recibidas, en orden.</summary>
    public List<ProcessRequest> Requests { get; } = [];

    /// <summary>Runner que devuelve siempre la salida indicada con el código dado.</summary>
    public static FakeProcessRunner Returning(string output, int exitCode = 0) =>
        new((_, _) => Task.FromResult(new ProcessResult { ExitCode = exitCode, StandardOutput = output }));

    /// <summary>Runner que siempre lanza la excepción indicada.</summary>
    public static FakeProcessRunner Throwing(Exception exception) =>
        new((_, _) => Task.FromException<ProcessResult>(exception));

    public Task<ProcessResult> ExecuteAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return _handler(request, cancellationToken);
    }
}
