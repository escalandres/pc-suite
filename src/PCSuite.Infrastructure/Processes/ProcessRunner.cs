using System.Diagnostics;
using System.Text;
using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.Processes;

/// <summary>
/// Implementación única de ejecución de procesos. Sin shell (UseShellExecute
/// = false) y con <see cref="ProcessStartInfo.ArgumentList"/>: los argumentos
/// llegan al sistema tal cual, sin interpretación ni posibilidad de
/// inyección. La salida se lee línea a línea en paralelo al proceso
/// (streaming) y además se conserva en el resultado.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> ExecuteAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException("FileName no puede estar vacío.", nameof(request));
        }

        if (request.Interactive &&
            (request.Timeout is not null || request.OnStandardOutput is not null || request.OnStandardError is not null))
        {
            // El modo interactivo no redirige: no hay streams que leer con
            // timeout ni callbacks que notificar.
            throw new ArgumentException(
                "Interactive no admite timeout ni callbacks de línea.",
                nameof(request));
        }

        if (request.RequiresElevation)
        {
            // La elevación llega con la Task 005; jamás elevamos por nuestra cuenta.
            throw new NotSupportedException("La elevación aún no está disponible (Task 005).");
        }

        return await ExecuteCoreAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProcessResult> ExecuteCoreAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,

            // Interactive: sin redirección y con ventana de consola propia
            // (creada aquí o heredada), para que la herramienta hable con el
            // usuario directamente. El resto de solicitudes son invisibles.
            RedirectStandardOutput = !request.Interactive,
            RedirectStandardError = !request.Interactive,
            CreateNoWindow = !request.Interactive,
            WorkingDirectory = request.WorkingDirectory ?? "",
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var timeoutCts = request.Timeout is { } timeout ? new CancellationTokenSource(timeout) : null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts?.Token ?? default);

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();

        if (!process.Start())
        {
            throw new InvalidOperationException("No se pudo iniciar el proceso.");
        }

        var stdoutBuffer = new StringBuilder();
        var stderrBuffer = new StringBuilder();

        // En modo interactivo no hay streams propias que bombear (la salida
        // va a la consola del proceso); los pumps quedan completados.
        var stdoutPump = request.Interactive
            ? Task.CompletedTask
            : PumpAsync(process.StandardOutput, stdoutBuffer, request.OnStandardOutput);
        var stderrPump = request.Interactive
            ? Task.CompletedTask
            : PumpAsync(process.StandardError, stderrBuffer, request.OnStandardError);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await DrainAsync(stdoutPump, stderrPump).ConfigureAwait(false);
            stopwatch.Stop();

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            // El timeout del request expiró: error tipado con mensaje controlado.
            throw new ProcessTimeoutException(request.Timeout!.Value);
        }

        await DrainAsync(stdoutPump, stderrPump).ConfigureAwait(false);
        stopwatch.Stop();

        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdoutBuffer.ToString(),
            StandardError = stderrBuffer.ToString(),
            Duration = stopwatch.Elapsed,
        };
    }

    /// <summary>Lee una stream completa línea a línea, notificando cada una conforme llega.</summary>
    private static async Task PumpAsync(StreamReader reader, StringBuilder buffer, Action<string>? onLine)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            buffer.AppendLine(line);
            onLine?.Invoke(line);
        }
    }

    private static async Task DrainAsync(Task stdoutPump, Task stderrPump)
    {
        try
        {
            await Task.WhenAll(stdoutPump, stderrPump).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Pipe cerrada por la terminación del proceso: es esperado.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit(2_000);
        }
        catch (InvalidOperationException)
        {
            // El proceso ya terminó.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Sin permisos o carrera con la salida: nada más que hacer.
        }
    }
}
