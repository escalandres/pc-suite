using System.Text.RegularExpressions;
using PCSuite.Core.Wsl;

namespace PCSuite.Infrastructure.Wsl;

/// <summary>
/// Parser de la salida real de wsl.exe (UTF-16LE, CRLF). Sólo lo que consume
/// la app: el valor de la primera línea con ':' (versión / distribución por
/// defecto) y las filas de `wsl --list --verbose`. Lo no reconocido se descarta
/// en silencio: el servicio devuelve listas parciales y nunca exceptiones por
/// formato de salida.
/// </summary>
internal static class WslParser
{
    /// <summary>
    /// Valor de la primera línea no vacía: el texto después del primer ':'
    /// (p. ej. "Versión de WSL: 2.7.14.0" → "2.7.14.0" o "Distribución
    /// predeterminada: docker-desktop" → "docker-desktop"). Sin ':' se devuelve
    /// la línea entera recortada; salida vacía → null.
    /// </summary>
    public static string? LineValue(string? output)
    {
        foreach (var line in (output ?? string.Empty).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var separator = trimmed.IndexOf(':');
            return separator >= 0 ? trimmed[(separator + 1)..].Trim() : trimmed;
        }

        return null;
    }

    /// <summary>
    /// Filas de `wsl --list --verbose`: columnas separadas por 2+ espacios con
    /// la distribución activa marcada por un prefijo `* ` que no forma parte
    /// del nombre; la cabecera la descarta el guard de versión ("VERSION" no es
    /// un entero) y cualquier fila con menos de nombre/estado/versión se
    /// ignora. Nunca lanza.
    /// </summary>
    public static IReadOnlyList<WslDistro> ParseList(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var distros = new List<WslDistro>();

        foreach (var line in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                trimmed = trimmed[2..].TrimStart();
            }

            var cells = Regex.Split(trimmed, @"\s{2,}");
            if (cells.Length < 3 || !int.TryParse(cells[^1], out var version) || version < 1)
            {
                continue;
            }

            distros.Add(new WslDistro(cells[0], string.Join(" ", cells[1..^1]), version));
        }

        return distros;
    }
}
