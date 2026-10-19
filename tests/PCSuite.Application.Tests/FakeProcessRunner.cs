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

    /// <summary>
    /// Runner que BOMBEA la salida línea a línea por OnStandardOutput (como
    /// el ProcessRunner real con los pipes) y luego devuelve el resultado.
    /// Para tests del progreso de freshclam (los escaneos ya no usan pipes:
    /// corren elevados con --log, ver <see cref="WritingScanLog"/>).
    /// </summary>
    public static FakeProcessRunner Pumping(string output, int exitCode = 0) =>
        new(
            (request, _) =>
            {
                foreach (var line in output.Split('\n'))
                {
                    request.OnStandardOutput?.Invoke(line.TrimEnd('\r'));
                }

                return Task.FromResult(
                    new ProcessResult { ExitCode = exitCode, StandardOutput = output });
            });

    /// <summary>
    /// Runner para ESCANEOS ELEVADOS: no hay pipes (runas no redirige), así
    /// que escribe la salida en el archivo --log= que lleva el argv —
    /// exactamente lo que hace clamscan real, y el servicio la taila y la
    /// devuelve como StandardOutput.
    /// </summary>
    public static FakeProcessRunner WritingScanLog(string output, int exitCode = 0) =>
        new(
            (request, _) =>
            {
                var logPath = ExtractScanLogPath(request);
                if (logPath is not null)
                {
                    File.WriteAllText(logPath, output);
                }

                return Task.FromResult(
                    new ProcessResult { ExitCode = exitCode, StandardOutput = string.Empty });
            });

    /// <summary>Runner que siempre lanza la excepción indicada.</summary>
    public static FakeProcessRunner Throwing(Exception exception) =>
        new((_, _) => Task.FromException<ProcessResult>(exception));

    /// <summary>Extrae la ruta del --log= del argv de un escaneo elevado.</summary>
    internal static string? ExtractScanLogPath(ProcessRequest request)
    {
        var argument = request.Arguments.FirstOrDefault(
            a => a.StartsWith("--log=", StringComparison.Ordinal));
        return argument is null ? null : argument["--log=".Length..];
    }

    public Task<ProcessResult> ExecuteAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return _handler(request, cancellationToken);
    }
}
