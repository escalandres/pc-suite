using System.Globalization;
using PCSuite.Core.ClamAV;

namespace PCSuite.Infrastructure.ClamAV;

/// <summary>Resumen ya separado de la salida de clamscan.</summary>
internal sealed record ClamAvScanParse(
    IReadOnlyList<ClamAvThreat> Threats,
    int? ScannedDirectories,
    int? ScannedFiles,
    int? InfectedFiles,
    int? KnownViruses,
    string? EngineVersion,
    bool HasSummary,
    bool HasScanActivity);

/// <summary>
/// Parseo CONTROLADO de la salida de clamscan (solo lectura, sin shell).
/// Tolera salida ausente o parcial: lo que no se reconoce simplemente se
/// ignora (WslParser, Task 007).
/// </summary>
internal static class ClamAvParser
{
    /// <summary>
    /// Tope de amenazas devueltas por escaneo: un escaneo completo puede
    /// producir decenas de miles de líneas "FOUND" y el resumen del propio
    /// clamscan sigue siendo la fuente de verdad de InfectedFiles
    /// (ponytail: sin lista paginada, la UI muestra el recuento exacto).
    /// </summary>
    private const int MaxThreats = 100;

    private const string VersionPrefix = "ClamAV ";

    private const string FoundSuffix = " FOUND";

    private const string KnownVirusesLabel = "Known viruses:";

    private const string EngineVersionLabel = "Engine version:";

    private const string ScannedDirectoriesLabel = "Scanned directories:";

    private const string ScannedFilesLabel = "Scanned files:";

    private const string InfectedFilesLabel = "Infected files:";

    /// <summary>
    /// "ClamAV 1.5.4/7123456 …" → motor 1.5.4 y build 7123456. Si la línea no
    /// tiene el formato esperado se devuelve la línea como versión y sin build
    /// de definiciones (nunca lanza).
    /// </summary>
    public static (string? Engine, string? Definitions) ParseVersion(string? output)
    {
        var line = FirstLine(output);
        if (line is null)
        {
            return (null, null);
        }

        if (!line.StartsWith(VersionPrefix, StringComparison.Ordinal))
        {
            return (line, null);
        }

        var token = line[VersionPrefix.Length..].Split(' ', 2)[0];
        var parts = token.Split('/', 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : (token, null);
    }

    /// <summary>Líneas "archivo: firma FOUND" + bloque SCAN SUMMARY (etiquetas siempre en inglés).</summary>
    public static ClamAvScanParse ParseScan(string? output)
    {
        var threats = new List<ClamAvThreat>();
        int? scannedDirectories = null;
        int? scannedFiles = null;
        int? infectedFiles = null;
        int? knownViruses = null;
        string? engineVersion = null;
        var hasSummary = false;
        var hasScanActivity = false;

        if (!string.IsNullOrEmpty(output))
        {
            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                // Task 025.4: actividad = el escaneo llegó a EMPEZAR (líneas
                // "Scanning …" o "archivo: OK"/FOUND) aunque el SCAN SUMMARY
                // nunca llegara — distingue "murió a mitad" de "falló antes
                // de arrancar".
                if (!hasScanActivity
                    && (line.StartsWith("Scanning ", StringComparison.Ordinal)
                        || line.EndsWith(": OK", StringComparison.Ordinal)
                        || line.EndsWith(FoundSuffix, StringComparison.Ordinal)))
                {
                    hasScanActivity = true;
                }

                if (line.EndsWith(FoundSuffix, StringComparison.Ordinal))
                {
                    var body = line[..^FoundSuffix.Length].TrimEnd();
                    if (threats.Count < MaxThreats)
                    {
                        // La ruta puede contener ": " (unidad C:\); la firma es
                        // lo que hay tras el ÚLTIMO ": ".
                        var separator = body.LastIndexOf(": ", StringComparison.Ordinal);
                        threats.Add(separator > 0
                            ? new ClamAvThreat(body[..separator], body[(separator + 2)..])
                            : new ClamAvThreat(body, string.Empty));
                    }

                    continue;
                }

                if (line.StartsWith(KnownVirusesLabel, StringComparison.Ordinal))
                {
                    knownViruses = IntValue(line, KnownVirusesLabel.Length);
                    hasSummary = true;
                }
                else if (line.StartsWith(EngineVersionLabel, StringComparison.Ordinal))
                {
                    engineVersion = line[EngineVersionLabel.Length..].Trim();
                    hasSummary = true;
                }
                else if (line.StartsWith(ScannedDirectoriesLabel, StringComparison.Ordinal))
                {
                    scannedDirectories = IntValue(line, ScannedDirectoriesLabel.Length);
                    hasSummary = true;
                }
                else if (line.StartsWith(ScannedFilesLabel, StringComparison.Ordinal))
                {
                    scannedFiles = IntValue(line, ScannedFilesLabel.Length);
                    hasSummary = true;
                }
                else if (line.StartsWith(InfectedFilesLabel, StringComparison.Ordinal))
                {
                    infectedFiles = IntValue(line, InfectedFilesLabel.Length);
                    hasSummary = true;
                }
            }
        }

        return new ClamAvScanParse(threats, scannedDirectories, scannedFiles, infectedFiles, knownViruses, engineVersion, hasSummary, hasScanActivity);
    }

    private static int? IntValue(string line, int labelLength) =>
        int.TryParse(line.AsSpan(labelLength), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string? FirstLine(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return null;
        }

        var line = output.Split('\n')[0].Trim();
        return line.Length == 0 ? null : line;
    }
}
