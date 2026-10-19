using PCSuite.Core.Uninstall;

namespace PCSuite.Infrastructure.Uninstall;

/// <summary>
/// Reglas duras de validación del plan de eliminación (Task 020). Se aplican
/// DOS veces por diseño: en el bridge (al construir el plan que ve el usuario)
/// y DENTRO del proceso elevado (revalidación independiente antes de tocar
/// nada — el payload del puente jamás se ejecuta tal cual).
///
/// Principios: rutas absolutas, nunca raíces del sistema, carpetas SIEMPRE
/// subcarpetas (jamás la raíz de Program Files o del perfil), claves solo en
/// raíces permitidas y servicios identificados por nombre seguro.
/// </summary>
public static class UninstallValidator
{
    /// <summary>Raíces permitidas para claves de registro del plan.</summary>
    internal static readonly string[] AllowedRegistryRoots =
    [
        .. UninstallCatalog.UninstallRoots,
        @"HKEY_LOCAL_MACHINE\SOFTWARE",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node",
        @"HKEY_CURRENT_USER\Software",
    ];

    /// <summary>
    /// Carpetas que jamás se ofrecen ni se borran: raíces del sistema y del
    /// usuario. Una eliminación válida SIEMPRE es una subcarpeta de algo.
    /// </summary>
    private static readonly string[] ForbiddenFolders =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
    ];

    /// <summary>¿Carpeta válida para eliminar? Absoluta (jamás relativa:
    /// una ruta relativa se resolvería contra CWD del helper, que NO es lo
    /// que el usuario vio en el plan), existente o no, pero JAMÁS una raíz
    /// protegida ni el propio disco.</summary>
    public static bool IsSafeFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = path.Trim();
        if (!Path.IsPathRooted(trimmed))
        {
            return false;
        }

        var full = Path.GetFullPath(trimmed);
        if (!Path.IsPathRooted(full))
        {
            return false;
        }

        // Raíz de disco (C:\) o exactamente igual a una protegida: jamás.
        if (full.EndsWith(Path.DirectorySeparatorChar)
            && full.Length <= 3 // "C:\"
            && char.IsLetter(full[0])
            && full[1] == ':')
        {
            return false;
        }

        foreach (var forbidden in ForbiddenFolders)
        {
            if (string.IsNullOrWhiteSpace(forbidden))
            {
                continue;
            }

            if (full.Equals(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Debe tener al menos UN nivel bajo una raíz (existe una carpeta padre).
        var parent = Path.GetDirectoryName(full);
        return !string.IsNullOrEmpty(parent) && parent!.Length > 3;
    }

    /// <summary>¿Clave de registro válida para el plan? Debe estar bajo una
    /// raíz permitida, sin '..' y con más de un nivel de profundidad.</summary>
    public static bool IsSafeRegistryKey(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath) || keyPath.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var normalized = keyPath.Trim();
        foreach (var root in AllowedRegistryRoots)
        {
            // Subclave de la raíz (jamás la raíz misma: borrar
            // HKLM\SOFTWARE entero sería catastrófico).
            if (normalized.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)
                && normalized.Length > root.Length + 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>¿Nombre de servicio/task seguro como identificador?
    /// Solo los caracteres que admite SCM, sin rutas ni separadores.</summary>
    public static bool IsSafeIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256)
        {
            return false;
        }

        foreach (var character in name)
        {
            var isSafe = char.IsAsciiLetterOrDigit(character)
                || character is '.' or '_' or '-' or ' ' or '(' or ')';
            if (!isSafe)
            {
                return false;
            }
        }

        return name.Any(char.IsAsciiLetterOrDigit);
    }

    /// <summary>¿Ruta de tarea programada segura? Empieza en '\' sin '..'.</summary>
    public static bool IsSafeTaskPath(string taskPath)
    {
        if (string.IsNullOrWhiteSpace(taskPath)
            || taskPath.Contains("..", StringComparison.Ordinal)
            || !taskPath.StartsWith('\\'))
        {
            return false;
        }

        return taskPath.Count(c => c == '\\') >= 2; // "\Carpeta\Tarea" mínimo
    }

    /// <summary>
    /// ¿Está una ruta DENTRO de otra carpeta (o es la misma)? Comparación por
    /// segmentos: "C:\Program Files\App" contiene "C:\Program Files\App\bin\svc.exe",
    /// pero NO "C:\Program Files\AppOtro". Es la base del matching
    /// no-por-nombre de servicios, tareas y procesos.
    /// </summary>
    public static bool IsPathInside(string path, string folder)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path.Trim());
            var fullFolder = Path.GetFullPath(folder.Trim());

            // La carpeta misma cuenta como "dentro" (frontera incluida).
            if (fullPath.Equals(fullFolder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!fullFolder.EndsWith(Path.DirectorySeparatorChar))
            {
                fullFolder += Path.DirectorySeparatorChar;
            }

            return fullPath.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
