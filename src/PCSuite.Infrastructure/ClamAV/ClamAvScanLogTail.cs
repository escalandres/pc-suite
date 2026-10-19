using System.Text;

namespace PCSuite.Infrastructure.ClamAV;

/// <summary>
/// Tail incremental del log de escaneo de clamscan (decisión del autor
/// 2026-10-18: los escaneos corren elevados con "runas", que no permite
/// redirigir stdout). El padre poll-ea el archivo mientras clamscan escribe:
/// lee solo los bytes NUEVOS desde el último offset y va entregando líneas
/// completas — la última línea a medias se conserva hasta su salto.
///
/// Tolerante por diseño: archivo aún no creado (UAC pendiente), IO
/// intermitente, o el proceso escribiendo mientras leemos
/// (FileShare.ReadWrite). Un fallo de lectura nunca rompe el escaneo: el
/// siguiente ciclo reintenta desde el offset ya avanzado.
/// </summary>
internal static class ClamAvScanLogTail
{
    /// <summary>
    /// Tope defensivo para una línea sin salto: un log de clamscan son líneas
    /// cortas; si algo escribe un megabyte sin '\n', se suelta como línea
    /// antes de crecer sin control.
    /// </summary>
    private const int MaxPartialChars = 1_048_576;

    /// <summary>
    /// Lee las líneas nuevas de <paramref name="path"/> desde
    /// <paramref name="offset"/> (que avanza). La cola parcial queda en
    /// <paramref name="partialLine"/> hasta ver su salto de línea.
    /// </summary>
    internal static IReadOnlyList<string> ReadNewLines(string path, ref long offset, StringBuilder partialLine)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return [];
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= offset)
            {
                return [];
            }

            stream.Seek(offset, SeekOrigin.Begin);
            var length = (int)Math.Min(stream.Length - offset, 8 * 1024 * 1024);
            var buffer = new byte[length];
            var read = 0;
            while (read < length)
            {
                var chunk = stream.Read(buffer, read, length - read);
                if (chunk <= 0)
                {
                    break;
                }

                read += chunk;
            }

            offset += read;
            if (read == 0)
            {
                return [];
            }

            partialLine.Append(Encoding.UTF8.GetString(buffer, 0, read));

            var lines = new List<string>();
            while (TryTakeLine(partialLine, out var line))
            {
                lines.Add(line);
            }

            // Tope defensivo: una "línea" gigante sin salto se suelta entera.
            if (partialLine.Length > MaxPartialChars)
            {
                lines.Add(partialLine.ToString());
                partialLine.Clear();
            }

            return lines;
        }
        catch (IOException)
        {
            // El proceso sigue escribiendo o el archivo cambió: el próximo
            // ciclo reintenta; jamás rompe el escaneo.
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Extrae la primera línea completa del buffer; respeta \r\n.</summary>
    private static bool TryTakeLine(StringBuilder partialLine, out string line)
    {
        for (var i = 0; i < partialLine.Length; i++)
        {
            if (partialLine[i] != '\n')
            {
                continue;
            }

            var takeLength = i;
            if (takeLength > 0 && partialLine[takeLength - 1] == '\r')
            {
                takeLength--;
            }

            line = partialLine.ToString(0, takeLength);
            var remainder = partialLine.ToString(i + 1, partialLine.Length - i - 1);
            partialLine.Clear();
            partialLine.Append(remainder);
            return true;
        }

        line = "";
        return false;
    }
}
