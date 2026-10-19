using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PCSuite.Core.Uninstall;

namespace PCSuite.Infrastructure.Uninstall;

/// <summary>
/// Construye el plan de eliminación de una app (Task 020) a partir de SU
/// PROPIA entrada de registro — jamás por patrones de nombre.
///
/// Matching quirúrgico:
/// - Procesos: ejecutable principal DENTRO de la carpeta de instalación.
/// - Servicios: ImagePath DENTRO de la carpeta de instalación (HKLM\Services).
/// - Tareas programadas: acción Exec DENTRO de la carpeta (Task Scheduler COM).
/// - Carpetas de datos con el nombre de la app: solo CANDIDATOS sin marcar
///   (heurística benigna: el usuario decide).
///
/// Solo lectura, sin elevación.
/// </summary>
public sealed class UninstallPlanner
{
    private readonly ILogger? _logger;

    public UninstallPlanner(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Si la app declara InstallLocation vacía no hay con qué derivar
    /// procesos/servicios/tareas: el plan se limita a la entrada de registro
    /// y a los candidatos de datos. Mejor honesto que shotgun.
    /// </summary>
    public RemovalPlan BuildPlan(InstalledAppInfo app)
    {
        var items = new List<RemovalPlanItem>();
        var installLocation = app.InstallLocation;

        if (!string.IsNullOrWhiteSpace(installLocation)
            && UninstallValidator.IsSafeFolder(installLocation))
        {
            // La carpeta de instalación: ítem derivado por excelencia.
            items.Add(new RemovalPlanItem
            {
                Kind = RemovalItemKind.Folder,
                Tier = RemovalItemTier.Derived,
                Identifier = installLocation,
                Detail = "Carpeta de instalación declarada por la app",
            });

            foreach (var process in FindProcessesInside(installLocation))
            {
                items.Add(new RemovalPlanItem
                {
                    Kind = RemovalItemKind.Process,
                    Tier = RemovalItemTier.Derived,
                    Identifier = $"{process.Id}:{process.Name}",
                    Detail = $"{process.Name} (PID {process.Id}) — ejecutable en la carpeta de instalación",
                });
            }

            foreach (var service in FindServicesInside(installLocation))
            {
                items.Add(new RemovalPlanItem
                {
                    Kind = RemovalItemKind.Service,
                    Tier = RemovalItemTier.Derived,
                    Identifier = service.Name,
                    Detail = $"Servicio — {service.ImagePath}",
                });
            }

            foreach (var task in FindTasksInside(installLocation))
            {
                items.Add(new RemovalPlanItem
                {
                    Kind = RemovalItemKind.ScheduledTask,
                    Tier = RemovalItemTier.Derived,
                    Identifier = task,
                    Detail = $"Tarea programada que ejecuta un binario de la app",
                });
            }
        }

        // Carpetas de datos candidatas: por NOMBRE de la app (heurística
        // benigna: van SIN marcar y con su riesgo a la vista del usuario).
        foreach (var (folder, description) in DataFolderCandidates(app.DisplayName))
        {
            items.Add(new RemovalPlanItem
            {
                Kind = RemovalItemKind.Folder,
                Tier = RemovalItemTier.Candidate,
                Identifier = folder,
                Detail = $"{description} (candidato: carpeta con el nombre de la app)",
            });
        }

        // Claves de residuo candidatas bajo SOFTWARE (vendedor/nombre).
        foreach (var key in RegistryKeyCandidates(app))
        {
            items.Add(new RemovalPlanItem
            {
                Kind = RemovalItemKind.RegistryKey,
                Tier = RemovalItemTier.Candidate,
                Identifier = key,
                Detail = "Candidato: clave con el nombre de la app",
            });
        }

        // La entrada de desinstalación misma: SIEMPRE derivada (es la que
        // mantiene la app viva en "Aplicaciones").
        items.Add(new RemovalPlanItem
        {
            Kind = RemovalItemKind.RegistryKey,
            Tier = RemovalItemTier.Derived,
            Identifier = app.RegistryKeyPath,
            Detail = "Entrada de desinstalación (la elimina de Aplicaciones y características)",
        });

        return new RemovalPlan
        {
            App = app,
            HasOfficialUninstaller =
                app.MsiGuid is not null || !string.IsNullOrWhiteSpace(app.UninstallString),
            Items = items,
        };
    }

    // ------------------------------------------------------------------
    // Procesos: el ejecutable principal vive dentro de la instalación.
    // ------------------------------------------------------------------

    private IReadOnlyList<(int Id, string Name)> FindProcessesInside(string installLocation)
    {
        var result = new List<(int, string)>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var mainModule = process.MainModule;
                if (mainModule?.FileName is { } exePath
                    && UninstallValidator.IsPathInside(exePath, installLocation))
                {
                    result.Add((process.Id, process.ProcessName));
                }
            }
            catch
            {
                // Procesos del sistema o de otro usuario: MainModule no es
                // accesible sin elevación. Se ignoran: si corresponde, la
                // limpieza elevada los re-examina.
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    // ------------------------------------------------------------------
    // Servicios: ImagePath (registro SCM) dentro de la instalación.
    // ------------------------------------------------------------------

    private IReadOnlyList<(string Name, string ImagePath)> FindServicesInside(string installLocation)
    {
        var result = new List<(string, string)>();
        try
        {
            using var services = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                "SYSTEM\\CurrentControlSet\\Services");
            if (services is null)
            {
                return result;
            }

            foreach (var serviceName in services.GetSubKeyNames())
            {
                if (!UninstallValidator.IsSafeIdentifier(serviceName))
                {
                    continue;
                }

                try
                {
                    using var serviceKey = services.OpenSubKey(serviceName);
                    var imagePath = serviceKey?.GetValue("ImagePath") as string;
                    if (string.IsNullOrWhiteSpace(imagePath))
                    {
                        continue;
                    }

                    var executable = StripServiceArguments(imagePath);
                    if (UninstallValidator.IsPathInside(executable, installLocation))
                    {
                        result.Add((serviceName, imagePath.Trim()));
                    }
                }
                catch
                {
                    // clave protegida: ignorar
                }
            }
        }
        catch (Exception exception)
        {
            _logger?.LogDebug(exception, "Enumerando servicios para el plan");
        }

        return result;
    }

    /// <summary>Quita argumentos y comillas del ImagePath de un servicio.</summary>
    private static string StripServiceArguments(string imagePath)
    {
        var trimmed = imagePath.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : trimmed.Trim('"');
        }

        var space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }

    // ------------------------------------------------------------------
    // Tareas programadas: Task Scheduler COM (Schedule.Service), sin
    // dependencias nuevas. Acción Exec apuntando a la instalación.
    // ------------------------------------------------------------------

    private IReadOnlyList<string> FindTasksInside(string installLocation)
    {
        var tasks = new List<string>();
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null)
            {
                return tasks;
            }

            dynamic service = Activator.CreateInstance(type)!;
            service.Connect();
            CollectTasks(service.GetFolder("\\"), installLocation, tasks);
        }
        catch (Exception exception)
        {
            _logger?.LogDebug(exception, "Enumerando tareas programadas para el plan");
        }

        return tasks;
    }

    private static void CollectTasks(dynamic folder, string installLocation, List<string> tasks)
    {
        foreach (dynamic task in folder.GetTasks(0)) // 0 = incluir ocultas
        {
            try
            {
                foreach (dynamic action in task.Definition.Actions)
                {
                    // ExecAction expone Path; otros tipos (COM/msgs) no aplican.
                    var actionPath = (string?)action.Path;
                    if (!string.IsNullOrEmpty(actionPath)
                        && UninstallValidator.IsPathInside(actionPath!, installLocation))
                    {
                        tasks.Add($"{task.Path}");
                        break;
                    }
                }
            }
            catch
            {
                // acción de otro tipo: ignorar
            }
        }

        foreach (dynamic subfolder in folder.GetFolders(0))
        {
            CollectTasks(subfolder, installLocation, tasks);
        }
    }

    // ------------------------------------------------------------------
    // Candidatos de datos/registro (heuras benignas, SIN marcar)
    // ------------------------------------------------------------------

    private static IEnumerable<(string Folder, string Description)> DataFolderCandidates(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            yield break;
        }

        var name = SanitizeFolderName(displayName);
        if (name.Length == 0)
        {
            yield break;
        }

        yield return (Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), name),
            "ProgramData");
        yield return (Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name),
            "AppData\\Local");
        yield return (Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), name),
            "AppData\\Roaming");
    }

    private static IEnumerable<string> RegistryKeyCandidates(InstalledAppInfo app)
    {
        if (string.IsNullOrWhiteSpace(app.DisplayName))
        {
            yield break;
        }

        var name = app.DisplayName.Trim();
        var publisher = app.Publisher.Trim();

        foreach (var root in (string[])
        [
            @"HKEY_LOCAL_MACHINE\SOFTWARE",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node",
            @"HKEY_CURRENT_USER\Software",
        ])
        {
            if (publisher.Length > 0)
            {
                yield return $"{root}\\{publisher}";
            }

            yield return $"{root}\\{name}";
        }
    }

    /// <summary>Nombre de carpeta sin caracteres ilegales (heura de candidatos).</summary>
    private static string SanitizeFolderName(string displayName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = displayName.Trim()
            .Select(c => invalid.Contains(c) ? '_' : c)
            .Take(80)
            .ToArray();
        return new string(chars).Trim('_');
    }
}
