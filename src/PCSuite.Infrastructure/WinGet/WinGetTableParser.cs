using System.Text.RegularExpressions;

namespace PCSuite.Infrastructure.WinGet;

/// <summary>
/// Parser POSICIONAL de las tablas de winget. winget solo imprime texto (no
/// admite --output json) y el locale cambia los nombres de columna, así que:
/// 1. se ancla en cada línea de separación de guiones (cabecera = línea inmediatamente anterior);
/// 2. se leen las posiciones de las palabras de la cabecera con cualquier whitespace;
/// 3. se cortan las filas por POSICIÓN — los espacios simples dentro de un
///    valor (nombres con espacios) no rompen la fila y las cabeceras
///    localizadas dan igual;
/// 4. se parsean TODAS las tablas del resultado (winget upgrade imprime dos)
///    y se descartan las filas cuya celda Id no parece un id de paquete
///    (banners, notas y "N actualizaciones disponibles." incluidos).
/// </summary>
internal static class WinGetTableParser
{
    private static readonly Regex TokenPattern = new(@"\S+", RegexOptions.Compiled);
    private static readonly Regex PackageIdLike =
        new("^[A-Za-z0-9][A-Za-z0-9._+-]*$", RegexOptions.Compiled);

    /// <summary>
    /// Filas válidas (una lista de celdas por cabecera: nombre, id, versión,
    /// 4ª columna, …) de TODAS las tablas del texto, en orden.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string? output)
    {
        var rows = new List<IReadOnlyList<string>>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return rows;
        }

        // "\r" suelto (progreso de winget) → salto de línea: queda la última
        // escritura, que es el estado final visible.
        var lines = output
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n');

        for (var separatorIndex = 1; separatorIndex < lines.Length; separatorIndex++)
        {
            if (!IsSeparator(lines[separatorIndex]))
            {
                continue;
            }

            var headerTokens = TokenPattern.Matches(lines[separatorIndex - 1]);
            if (headerTokens.Count < 3)
            {
                continue; // no es una cabecera de tabla de paquetes (nombre/id/versión)
            }

            var nameStart = headerTokens[0].Index;
            var idStart = headerTokens[1].Index;
            var versionStart = headerTokens[2].Index;
            var versionEnd = headerTokens.Count >= 4 ? headerTokens[3].Index : int.MaxValue;

            // Filas hasta la línea en blanco o el siguiente separador.
            for (var rowIndex = separatorIndex + 1; rowIndex < lines.Length; rowIndex++)
            {
                var line = lines[rowIndex];
                if (string.IsNullOrWhiteSpace(line) || IsSeparator(line))
                {
                    break;
                }

                if (line.Length <= versionStart)
                {
                    continue; // no llega a la columna de versión: resumen/nota
                }

                var name = Slice(line, nameStart, idStart);
                var id = Slice(line, idStart, versionStart);
                var version = Slice(line, versionStart, versionEnd);

                if (name.Length == 0 || version.Length == 0 || !PackageIdLike.IsMatch(id))
                {
                    continue; // fila que no es un paquete
                }

                var cells = new string[headerTokens.Count];
                for (var column = 0; column < headerTokens.Count; column++)
                {
                    var start = headerTokens[column].Index;
                    var end = column + 1 < headerTokens.Count
                        ? headerTokens[column + 1].Index
                        : line.Length;
                    cells[column] = column switch
                    {
                        0 => name,
                        1 => id,
                        2 => version,
                        _ => Slice(line, start, end),
                    };
                }

                rows.Add(cells);
            }
        }

        return rows;
    }

    /// <summary>Corte posicional seguro: start/end fuera de rango → celda vacía.</summary>
    private static string Slice(string line, int start, int end)
    {
        if (start >= line.Length || end <= start)
        {
            return string.Empty;
        }

        return line.Substring(start, Math.Min(end, line.Length) - start).Trim();
    }

    private static bool IsSeparator(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 5 && trimmed.All(character => character == '-');
    }
}
