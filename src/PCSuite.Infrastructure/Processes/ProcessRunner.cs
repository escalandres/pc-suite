using System.Diagnostics;
using System.Text;
using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.Processes;

/// <summary>
/// Implementación única de ejecución de procesos. Sin shell (UseShellExecute
/// = false) y con <see cref="ProcessStartInfo.ArgumentList"/>: los argumentos
/// llegan al sistema tal cual, sin interpretación ni posibilidad de
/// inyección. La única excepción es la elevación por operación (Task 005),
/// que usa ShellExecute con "runas" y compone su propia línea de argumentos.
/// La salida se lee línea a línea en paralelo al proceso
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
            // Contratos de la elevación (Task 005): ShellExecute("runas") no
            // admite redirección de streams ni modos interactivos, y una ruta
            // relativa se resolvería por PATH/CWD — elevado, eso es un vector
            // de ejecución: exigimos ruta absoluta.
            if (request.Interactive)
            {
                throw new ArgumentException("La elevación no admite modo interactivo.", nameof(request));
            }

            if (request.OnStandardOutput is not null || request.OnStandardError is not null)
            {
                throw new ArgumentException("La elevación no admite callbacks de línea.", nameof(request));
            }

            if (!Path.IsPathRooted(request.FileName))
            {
                throw new ArgumentException("La elevación exige una ruta absoluta de FileName.", nameof(request));
            }
        }

        return await ExecuteCoreAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProcessResult> ExecuteCoreAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        // Con redirección solo donde puede haberla: fuera de modo interactivo
        // (la herramienta habla con el usuario) y de elevación (ShellExecute).
        var captureOutput = !request.Interactive && !request.RequiresElevation;

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = request.RequiresElevation,

            // Interactive: sin redirección y con ventana de consola propia
            // (creada aquí o heredada), para que la herramienta hable con el
            // usuario directamente. El resto de solicitudes son invisibles.
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            CreateNoWindow = captureOutput,
            WorkingDirectory = request.WorkingDirectory ?? "",
        };

        if (request.RequiresElevation)
        {
            // UAC: ShellExecuteEx con "runas" (el prompt lo genera Windows).
            // UseShellExecute no acepta ArgumentList: la línea se compone a
            // mano con las reglas de CommandLineToArgvW (BuildShellArguments).
            startInfo.Verb = "runas";
            startInfo.Arguments = BuildShellArguments(request.Arguments);
        }
        else
        {
            foreach (var argument in request.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        using var timeoutCts = request.Timeout is { } timeout ? new CancellationTokenSource(timeout) : null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts?.Token ?? default);

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("No se pudo iniciar el proceso.");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
            when (request.RequiresElevation && exception.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: el usuario descartó el prompt UAC.
            throw new ElevationDeclinedException();
        }

        var stdoutBuffer = new StringBuilder();
        var stderrBuffer = new StringBuilder();

        // Sin redirección no hay streams propias que bombear (la salida va a
        // la consola del proceso); los pumps quedan completados.
        var stdoutPump = captureOutput
            ? PumpAsync(process.StandardOutput, stdoutBuffer, request.OnStandardOutput)
            : Task.CompletedTask;
        var stderrPump = captureOutput
            ? PumpAsync(process.StandardError, stderrBuffer, request.OnStandardError)
            : Task.CompletedTask;

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

    /// <summary>
    /// Compone la línea de argumentos para ShellExecute siguiendo las reglas
    /// de CommandLineToArgvW (necesario porque UseShellExecute=true no acepta
    /// <see cref="ProcessStartInfo.ArgumentList"/>). Cada argumento se envuelve
    /// entre comillas; dentro, las comillas se escapan con barra invertida y
    /// las barras invertidas terminales se duplican. Un argumento vacío viaja
    /// como "" para no colapsar con el vecino.
    /// </summary>
    public static string BuildShellArguments(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var parts = arguments.Select(QuoteArgument).ToArray();
        return string.Join(' ', parts);
    }

    private static string QuoteArgument(string argument)
    {
        var builder = new StringBuilder(argument.Length + 2);
        builder.Append('"');

        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                // Barra invertidas pendientes: se duplican y la comilla se
                // escapa con una más.
                builder.Append('\\', (backslashes * 2) + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            builder.Append('\\', backslashes);
            builder.Append(character);
            backslashes = 0;
        }

        // Barra invertidas terminales: se duplican antes de cerrar la comilla.
        builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
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
