using System.Globalization;
using System.Text;

namespace PCSuite.MockProcess;

/// <summary>
/// Proceso mínimo y controlable para probar el ProcessRunner/JobEngine sin
/// depender de software instalado en la máquina (node, powershell, winget...).
///
/// Verbos:
///   out [n] [delayMs]   → n líneas "stdout-line-i" en stdout (con pausa opcional entre ellas)
///   err [n] [delayMs]   → n líneas "stderr-line-i" en stderr
///   exit &lt;code&gt;        → sale con ese código
///   sleep &lt;ms&gt;          → duerme (para probar timeout/cancelación)
///   echo &lt;args...&gt;      → imprime "arg:X" por cada argumento recibido
///   cwd                → imprime el directorio de trabajo actual
///   all &lt;code&gt;          → 2 líneas stdout + 1 stderr y sale con ese código
///   utf8               → escribe bytes UTF-8 crudos en stdout (como winget)
///   utf16              → escribe bytes UTF-16LE crudos en stdout (como wsl)
///   (sin argumentos)    → "mock-process ready", sale con 0
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("mock-process ready");
            return 0;
        }

        switch (args[0])
        {
            case "out":
            case "err":
                return await WriteLinesAsync(args);

            case "exit":
                return TryInt(args, 1, out var exitCode) ? exitCode : 64;

            case "sleep":
                if (!TryInt(args, 1, out var sleepMs))
                {
                    return 64;
                }

                await Task.Delay(sleepMs);
                return 0;

            case "echo":
                for (var i = 1; i < args.Length; i++)
                {
                    Console.WriteLine($"arg:{args[i]}");
                }

                return 0;

            case "cwd":
                Console.WriteLine(Environment.CurrentDirectory);
                return 0;

            case "all":
                Console.WriteLine("stdout-line-1");
                Console.WriteLine("stdout-line-2");
                Console.Error.WriteLine("stderr-line-1");
                return TryInt(args, 1, out var allExitCode) ? allExitCode : 64;

            case "utf8":
                // Bytes UTF-8 crudos por la tubería, saltándose el writer de
                // texto de consola (codepage OEM): exactamente lo que hace
                // winget. Decodificar con OutputEncoding = UTF8 da el texto
                // original; sin él, mojibake.
                await Console.OpenStandardOutput().WriteAsync(
                    Encoding.UTF8.GetBytes("Versión 1.2.3 — muñeco\r\n"));
                return 0;

            case "utf16":
                // Igual que utf8 pero en UTF-16LE: exactamente lo que escribe
                // wsl.exe (sin BOM). Encoding.Unicode.GetBytes no incluye el
                // preámbulo, así que los bytes arrancan en el primer carácter.
                await Console.OpenStandardOutput().WriteAsync(
                    Encoding.Unicode.GetBytes("Versión 2.7.14 — muñeco\r\n"));
                return 0;

            default:
                Console.Error.WriteLine($"unknown verb: {args[0]}");
                return 64;
        }
    }

    private static async Task<int> WriteLinesAsync(string[] args)
    {
        if (!TryInt(args, 1, out var count))
        {
            return 64;
        }

        var delay = args.Length > 2 && TryInt(args, 2, out var delayMs) ? delayMs : 0;
        var isError = args[0] == "err";

        for (var i = 1; i <= count; i++)
        {
            if (isError)
            {
                Console.Error.WriteLine($"stderr-line-{i}");
            }
            else
            {
                Console.WriteLine($"stdout-line-{i}");
            }

            if (delay > 0)
            {
                await Task.Delay(delay);
            }
        }

        return 0;
    }

    private static bool TryInt(string[] args, int index, out int value)
    {
        if (index >= args.Length)
        {
            value = 0;
            return false;
        }

        return int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
