using System.Runtime.Versioning;
using Microsoft.Win32;
using PCSuite.Core.Uninstall;

namespace PCSuite.Infrastructure.Uninstall;

/// <summary>
/// Catálogo de apps instaladas según el registro de desinstalación de
/// Windows (Task 020): la fuente que usa "Aplicaciones y características".
/// Enumera las tres colmenas — HKLM 64/32 bits y HKCU — y por eso VE apps
/// que winget no conoce: instaladores caseros, portables con instalador,
/// apps heredadas y mal registradas.
///
/// Solo lectura, sin elevación: HKLM se lee con derechos de usuario.
/// Windows-only por naturaleza (el registro de Windows ES el dominio).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UninstallCatalog
{
    /// <summary>Raíces canónicas de entradas de desinstalación.</summary>
    internal static readonly string[] UninstallRoots =
    [
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    /// <summary>
    /// Lista todas las apps registradas con DisplayName (sin SystemComponent
    /// por defecto; appwiz también las oculta). Orden alfabético.
    /// </summary>
    public IReadOnlyList<InstalledAppInfo> ListApps(bool includeSystemComponents = false)
    {
        var apps = new List<InstalledAppInfo>();
        foreach (var root in UninstallRoots)
        {
            using var hive = OpenSubKey(root);
            if (hive is null)
            {
                continue;
            }

            foreach (var subKeyName in hive.GetSubKeyNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                var keyPath = root + "\\" + subKeyName;
                using var subKey = hive.OpenSubKey(subKeyName);
                if (subKey is null)
                {
                    continue;
                }

                var app = ReadEntry(keyPath, subKey);
                if (app is null || (app.IsSystemComponent && !includeSystemComponents))
                {
                    continue;
                }

                apps.Add(app);
            }
        }

        return apps
            .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Lee UNA entrada por su ruta de clave completa. Null si no existe o no
    /// tiene DisplayName. La ruta se valida contra las raíces fijas.
    /// </summary>
    public InstalledAppInfo? GetApp(string registryKeyPath)
    {
        if (!IsAllowedKeyPath(registryKeyPath))
        {
            return null;
        }

        using var key = OpenSubKey(registryKeyPath);
        return key is null ? null : ReadEntry(registryKeyPath, key);
    }

    /// <summary>¿La clave pertenece a una raíz de desinstalación permitida?</summary>
    internal static bool IsAllowedKeyPath(string registryKeyPath)
    {
        if (string.IsNullOrWhiteSpace(registryKeyPath))
        {
            return false;
        }

        var normalized = registryKeyPath.Trim();
        foreach (var root in UninstallRoots)
        {
            if (normalized.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
            {
                // Sin '..' y sin más de un nivel de restriccion rara:
                // el nombre de subclave es del instalador, pero la ruta jamás
                // puede salir de la raíz.
                return !normalized.Contains("..", StringComparison.Ordinal)
                    && normalized.Length > root.Length + 1;
            }
        }

        return false;
    }

    private static RegistryKey? OpenSubKey(string keyPath)
    {
        var (hiveName, subPath) = SplitKeyPath(keyPath);
        if (hiveName is null)
        {
            return null;
        }

        using var hive = hiveName switch
        {
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine as RegistryKey,
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            _ => null,
        };
        return hive?.OpenSubKey(subPath);
    }

    private static (string? Hive, string SubPath) SplitKeyPath(string keyPath)
    {
        var separator = keyPath.IndexOf('\\');
        if (separator <= 0)
        {
            return (null, "");
        }

        return (keyPath[..separator], keyPath[(separator + 1)..]);
    }

    private static InstalledAppInfo? ReadEntry(string keyPath, RegistryKey key)
    {
        var displayName = key.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            // Sin DisplayName no aparece en appwiz y no es desinstalable:
            // ignorar (ruido de instaladores mal hechos).
            return null;
        }

        var uninstallString = key.GetValue("UninstallString") as string ?? "";
        var msiGuid = ExtractMsiGuid(key.Name, uninstallString);

        return new InstalledAppInfo
        {
            RegistryKeyPath = keyPath,
            DisplayName = displayName.Trim(),
            Publisher = (key.GetValue("Publisher") as string ?? "").Trim(),
            DisplayVersion = (key.GetValue("DisplayVersion") as string ?? "").Trim(),
            InstallLocation = (key.GetValue("InstallLocation") as string ?? "").Trim(),
            UninstallString = uninstallString.Trim(),
            MsiGuid = msiGuid,
            EstimatedSizeKb = key.GetValue("EstimatedSize") is int estimated ? estimated : 0,
            IsSystemComponent = string.Equals(
                key.GetValue("SystemComponent") as string ?? key.GetValue("SystemComponent")?.ToString(),
                "1",
                StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>GUID de MSI: la subclave ES un GUID (instalación MSI per-machine).</summary>
    private static string? ExtractMsiGuid(string keyName, string uninstallString)
    {
        var name = keyName[(keyName.LastIndexOf('\\') + 1)..];
        var guid = TryAsGuid(name);
        if (guid is not null)
        {
            return guid;
        }

        // "MsiExec.exe /I{GUID}" o "/X{GUID}": el uninstaller registrado de MSI.
        foreach (var token in uninstallString.Split(' '))
        {
            var braceStart = token.IndexOf('{');
            var braceEnd = token.IndexOf('}');
            if (braceStart >= 0 && braceEnd > braceStart)
            {
                guid = TryAsGuid(token[braceStart..(braceEnd + 1)]);
                if (guid is not null)
                {
                    return guid;
                }
            }
        }

        return null;
    }

    private static string? TryAsGuid(string candidate)
    {
        return Guid.TryParseExact(candidate, "B", out var guid)
            ? guid.ToString("B")
            : null;
    }
}
