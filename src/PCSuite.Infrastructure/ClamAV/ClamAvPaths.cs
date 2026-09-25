using PCSuite.Infrastructure.Developers;

namespace PCSuite.Infrastructure.ClamAV;

/// <summary>
/// Localización de clamscan.exe/freshclam.exe: la PATH primero
/// (<see cref="ExecutableLocator"/>) y después los directorios donde el
/// instalador de Cisco.ClamAV (MSI vía winget) deja los binarios. Los tests
/// inyectan lista vacía para no depender del equipo donde corren.
/// </summary>
internal static class ClamAvPaths
{
    /// <summary>Directorios por omisión (se consultan en cada operación).</summary>
    public static IReadOnlyList<string> DefaultInstallDirectories() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClamAV"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ClamAV"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClamAV"),
    ];

    public static string? FindClamScan(ExecutableLocator locator, IReadOnlyList<string> installDirectories)
    {
        var inPath = locator.Find("clamscan");
        if (inPath is not null)
        {
            return inPath;
        }

        foreach (var directory in installDirectories)
        {
            foreach (var candidate in new[]
            {
                Path.Combine(directory, "clamscan.exe"),
                Path.Combine(directory, "bin", "clamscan.exe"),
            })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>freshclam.exe vive junto a clamscan.exe; null si no existe.</summary>
    public static string? FindFreshclam(string clamScanPath)
    {
        var directory = Path.GetDirectoryName(clamScanPath);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var candidate = Path.Combine(directory, "freshclam.exe");
        return File.Exists(candidate) ? candidate : null;
    }
}
