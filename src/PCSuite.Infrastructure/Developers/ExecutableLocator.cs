namespace PCSuite.Infrastructure.Developers;

/// <summary>
/// Localiza ejecutables en la variable PATH como harría <c>where.exe</c>:
/// para cada directorio, en orden de extensiones ejecutables. Los archivos
/// sin extensión se ignoran porque en Windows suelen ser shims POSIX (p. ej.
/// el <c>pnpm</c> de la instalación con Corepack) que no se pueden lanzar.
/// </summary>
public sealed class ExecutableLocator
{
    private static readonly string[] ExecutableExtensions = [".exe", ".com", ".cmd", ".bat"];

    private readonly string[] _directories;

    /// <param name="pathVariable">
    /// Contenido de la PATH; null usa la PATH real del proceso (en tests se
    /// inyecta una ruta temporal para no depender del equipo).
    /// </param>
    public ExecutableLocator(string? pathVariable = null)
    {
        var raw = pathVariable
            ?? Environment.GetEnvironmentVariable("PATH")
            ?? string.Empty;
        raw = Environment.ExpandEnvironmentVariables(raw);

        _directories = raw
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim().Trim('"'))
            .Where(entry => entry.Length > 0)
            .ToArray();
    }

    /// <summary>Ruta completa del ejecutable, o null si no está en la PATH.</summary>
    public string? Find(string baseName)
    {
        foreach (var directory in _directories)
        {
            foreach (var extension in ExecutableExtensions)
            {
                var candidate = Path.Combine(directory, baseName + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
