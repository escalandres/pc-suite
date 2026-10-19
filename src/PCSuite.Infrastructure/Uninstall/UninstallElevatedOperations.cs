using System.Diagnostics;
using Microsoft.Win32;
using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Operations;
using PCSuite.Core.Uninstall;
using PCSuite.Infrastructure.Operations;

namespace PCSuite.Infrastructure.Uninstall;

/// <summary>
/// Lanza el DESINSTALADOR OFICIAL de la app dentro del proceso elevado
/// (Task 020). Seguridad: el payload solo porta la ruta de la clave de
/// registro; el comando se RELEE del registro AQUÍ (jamás viaja por el
/// puente). Es lo mismo que ejecuta appwiz.cpl — con la elevación de más.
///
/// MSI: msiexec /x {GUID} interactivo (GUID revalidado). Instaladores
/// propios: exe absoluto existente + sus argumentos, en ventana propia.
/// No espera al desinstalador: el asistente habla con el usuario y la app
/// re-descubre los residuos después.
/// </summary>
public sealed class UninstallRunOfficialElevatedOperation : IElevatedOperation
{
    private readonly UninstallCatalog _catalog = new();

    public string Id => ElevatedOperationIds.UninstallRunOfficial;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out UninstallKeyPayload payload)
            || payload is null || string.IsNullOrWhiteSpace(payload.RegistryKeyPath))
        {
            return Task.FromResult(OperationResult.Fail(
                BridgeErrorCodes.MalformedMessage,
                "El payload debe ser { registryKeyPath }."));
        }

        var app = _catalog.GetApp(payload.RegistryKeyPath);
        if (app is null)
        {
            return Task.FromResult(OperationResult.Fail(
                BridgeErrorCodes.UninstallFailed,
                "La entrada de desinstalación ya no existe."));
        }

        try
        {
            // 1) MSI: camino limpio con GUID revalidado.
            if (app.MsiGuid is { } guid && Guid.TryParseExact(guid, "B", out _))
            {
                if (string.IsNullOrWhiteSpace(app.UninstallString))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "msiexec.exe",
                        Arguments = $"/x {guid}",
                        UseShellExecute = true,
                    });
                    return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(
                        new OfficialUninstallResultDto { Launched = true, Launcher = "msiexec" })));
                }
            }

            // 2) UninstallString propio: exe absoluto + args, ventana visible.
            var (executable, arguments) = SplitCommand(app.UninstallString);
            if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable) || !File.Exists(executable))
            {
                return Task.FromResult(OperationResult.Fail(
                    BridgeErrorCodes.UninstallFailed,
                    "El desinstalador registrado no existe o no es una ruta absoluta."));
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = true, // ventana propia: el asistente es interactivo
            });

            return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(
                new OfficialUninstallResultDto { Launched = true, Launcher = Path.GetFileName(executable) })));
        }
        catch (Exception)
        {
            return Task.FromResult(OperationResult.Fail(
                BridgeErrorCodes.UninstallFailed,
                "No se pudo lanzar el desinstalador oficial."));
        }
    }

    /// <summary>Divide "exe ruta absoluta" + argumentos (comillas respetadas).</summary>
    internal static (string Executable, string Arguments) SplitCommand(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.Length == 0)
        {
            return ("", "");
        }

        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            if (end > 1)
            {
                return (trimmed[1..end], trimmed[(end + 1)..].Trim());
            }
        }

        var space = trimmed.IndexOf(' ');
        return space < 0 ? (trimmed, "") : (trimmed[..space], trimmed[(space + 1)..].Trim());
    }
}

/// <summary>
/// Limpieza de residuos DENTRO del proceso elevado (Task 020). El payload
/// porta los ítems aprobados por el usuario, pero NADA se ejecuta tal cual:
/// cada ítem se REVALIDA aquí (rutas absolutas, jamás raíces del sistema,
/// servicios re-verificados por contención de ruta contra las carpetas del
/// plan). Orden: procesos → servicios → tareas → carpetas → registro.
/// </summary>
public sealed class UninstallCleanupElevatedOperation : IElevatedOperation
{
    public string Id => ElevatedOperationIds.UninstallCleanup;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out CleanupPayload payload)
            || payload?.Items is null || payload.Items.Count == 0)
        {
            return Task.FromResult(OperationResult.Fail(
                BridgeErrorCodes.MalformedMessage,
                "El payload debe ser { items: [{ kind, value }] }."));
        }

        var results = new List<RemovalItemResult>();
        var requiresRestart = false;

        // Carpetas aprobadas: base para re-verificar procesos/servicios por
        // contención (la segunda barrera contra identificadores mentirosos).
        var approvedFolders = payload.Items
            .Where(i => i.Kind == nameof(RemovalItemKind.Folder).ToLowerInvariant())
            .Select(i => i.Value)
            .Where(UninstallValidator.IsSafeFolder)
            .ToList();

        foreach (var item in payload.Items)
        {
            switch (item.Kind)
            {
                case "process":
                    KillProcess(item.Value, approvedFolders, results);
                    break;
                case "service":
                    requiresRestart |= RemoveService(item.Value, approvedFolders, results);
                    break;
                case "scheduledtask":
                    RemoveTask(item.Value, results);
                    break;
                case "folder":
                    RemoveFolder(item.Value, results);
                    break;
                case "registrykey":
                    RemoveRegistryKey(item.Value, results);
                    break;
                default:
                    results.Add(new RemovalItemResult
                    {
                        Kind = RemovalItemKind.Process,
                        Identifier = item.Kind ?? "?",
                        Succeeded = false,
                        Detail = "Tipo de ítem desconocido: ignorado.",
                    });
                    break;
            }
        }

        return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(new RemovalOutcomeDto
        {
            Results = [.. results.Select(r => new RemovalItemResultDto
            {
                Kind = r.Kind.ToString().ToLowerInvariant(),
                Identifier = r.Identifier,
                Succeeded = r.Succeeded,
                Detail = r.Detail,
            })],
            RequiresRestart = requiresRestart,
        })));
    }

    private static void KillProcess(string identifier, List<string> approvedFolders, List<RemovalItemResult> results)
    {
        // "pid:nombre" — revalidar que SU módulo siga dentro de las carpetas.
        var separator = identifier.IndexOf(':');
        if (separator <= 0 || !int.TryParse(identifier[..separator], out var pid))
        {
            results.Add(Failed(RemovalItemKind.Process, identifier, "Identificador de proceso inválido."));
            return;
        }

        try
        {
            var process = Process.GetProcessById(pid);
            try
            {
                var module = process.MainModule?.FileName;
                if (module is null || !approvedFolders.Any(f => UninstallValidator.IsPathInside(module, f)))
                {
                    results.Add(Failed(RemovalItemKind.Process, identifier,
                        "El proceso ya no corresponde a la app (no se tocó)."));
                    return;
                }

                process.Kill(entireProcessTree: true);
                results.Add(Succeeded(RemovalItemKind.Process, identifier));
            }
            finally
            {
                process.Dispose();
            }
        }
        catch (ArgumentException)
        {
            // El proceso ya se fue: misión cumplida de facto.
            results.Add(Succeeded(RemovalItemKind.Process, identifier));
        }
        catch (Exception)
        {
            results.Add(Failed(RemovalItemKind.Process, identifier, "No se pudo terminar (¿elevado por el sistema?)."));
        }
    }

    private static bool RemoveService(string serviceName, List<string> approvedFolders, List<RemovalItemResult> results)
    {
        if (!UninstallValidator.IsSafeIdentifier(serviceName))
        {
            results.Add(Failed(RemovalItemKind.Service, serviceName, "Nombre de servicio no válido."));
            return false;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $"SYSTEM\\CurrentControlSet\\Services\\{serviceName}");
            if (key is null)
            {
                results.Add(Succeeded(RemovalItemKind.Service, serviceName));
                return false;
            }

            // Re-verificación por contención: el ImagePath debe seguir
            // apuntando a las carpetas aprobadas del plan.
            var imagePath = key.GetValue("ImagePath") as string;
            var executable = UninstallRunOfficialElevatedOperation.SplitCommand(imagePath ?? "").Executable;
            if (!approvedFolders.Any(f => UninstallValidator.IsPathInside(executable, f)))
            {
                results.Add(Failed(RemovalItemKind.Service, serviceName,
                    "El servicio apunta fuera de las carpetas aprobadas: no se tocó."));
                return false;
            }

            StopServiceByScm(serviceName);
            var marked = DeleteServiceByScm(serviceName);
            results.Add(Succeeded(RemovalItemKind.Service, serviceName));
            return marked;
        }
        catch (Exception)
        {
            results.Add(Failed(RemovalItemKind.Service, serviceName, "No se pudo eliminar el servicio."));
            return false;
        }
    }

    /// <summary>Parar y borrar vía SCM directo (sin sc.exe: sin shell).</summary>
    private static void StopServiceByScm(string serviceName)
    {
        try
        {
            using var controller = new System.ServiceProcess.ServiceController(serviceName);
            if (controller.Status == System.ServiceProcess.ServiceControllerStatus.Running)
            {
                controller.Stop();
                controller.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            }
        }
        catch
        {
            // parado ya, o inalcanzable: el delete decide
        }
    }

    private static bool DeleteServiceByScm(string serviceName)
    {
        // DeleteService marca "para borrado en el próximo arranque" si hay
        // handles abiertos; se reporta al usuario como requiere-reinicio.
        return Native.DeleteService(serviceName);
    }

    private static void RemoveTask(string taskPath, List<RemovalItemResult> results)
    {
        if (!UninstallValidator.IsSafeTaskPath(taskPath))
        {
            results.Add(Failed(RemovalItemKind.ScheduledTask, taskPath, "Ruta de tarea no válida."));
            return;
        }

        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            dynamic service = Activator.CreateInstance(type!)!;
            service.Connect();
            var name = taskPath[(taskPath.LastIndexOf('\\') + 1)..];
            var folder = taskPath[..taskPath.LastIndexOf('\\')];
            var parent = folder.Length == 0 ? service.GetFolder("\\") : service.GetFolder(folder);
            parent.DeleteTask(name, 0); // 0: no excepción si no existe
            results.Add(Succeeded(RemovalItemKind.ScheduledTask, taskPath));
        }
        catch (Exception)
        {
            results.Add(Failed(RemovalItemKind.ScheduledTask, taskPath, "No se pudo eliminar la tarea."));
        }
    }

    private static void RemoveFolder(string path, List<RemovalItemResult> results)
    {
        if (!UninstallValidator.IsSafeFolder(path))
        {
            results.Add(Failed(RemovalItemKind.Folder, path, "Carpeta protegida o ruta no válida: no se tocó."));
            return;
        }

        try
        {
            if (!Directory.Exists(path))
            {
                results.Add(Succeeded(RemovalItemKind.Folder, path));
                return;
            }

            Directory.Delete(path, recursive: true);
            results.Add(Succeeded(RemovalItemKind.Folder, path));
        }
        catch (Exception)
        {
            results.Add(Failed(RemovalItemKind.Folder, path,
                "No se pudo borrar por completo (¿archivos en uso?). Reintenta tras cerrar la app."));
        }
    }

    private static void RemoveRegistryKey(string keyPath, List<RemovalItemResult> results)
    {
        if (!UninstallValidator.IsSafeRegistryKey(keyPath))
        {
            results.Add(Failed(RemovalItemKind.RegistryKey, keyPath, "Clave fuera de las raíces permitidas: no se tocó."));
            return;
        }

        try
        {
            var (hive, subPath) = SplitRegistryPath(keyPath);
            // OJO: hive es un singleton (Registry.LocalMachine/CurrentUser):
            // jamás se dispone.
            if (hive is null)
            {
                results.Add(Failed(RemovalItemKind.RegistryKey, keyPath, "Colmena no reconocida."));
                return;
            }

            if (hive.OpenSubKey(subPath) is null)
            {
                results.Add(Succeeded(RemovalItemKind.RegistryKey, keyPath));
                return;
            }

            hive.DeleteSubKeyTree(subPath);
            results.Add(Succeeded(RemovalItemKind.RegistryKey, keyPath));
        }
        catch (Exception)
        {
            results.Add(Failed(RemovalItemKind.RegistryKey, keyPath, "No se pudo eliminar la clave."));
        }
    }

    private static (RegistryKey? Hive, string SubPath) SplitRegistryPath(string keyPath)
    {
        var separator = keyPath.IndexOf('\\');
        if (separator <= 0)
        {
            return (null, "");
        }

        var hiveName = keyPath[..separator];
        var subPath = keyPath[(separator + 1)..];
        return hiveName.ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" => (Registry.LocalMachine, subPath),
            "HKEY_CURRENT_USER" => (Registry.CurrentUser, subPath),
            _ => (null, ""),
        };
    }

    private static RemovalItemResult Succeeded(RemovalItemKind kind, string identifier) => new()
    {
        Kind = kind,
        Identifier = identifier,
        Succeeded = true,
    };

    private static RemovalItemResult Failed(RemovalItemKind kind, string identifier, string detail) => new()
    {
        Kind = kind,
        Identifier = identifier,
        Succeeded = false,
        Detail = detail,
    };

    /// <summary>SCM DeleteService por P/Invoke: sin procesos externos.</summary>
    private static class Native
    {
        private const uint ServiceAllAccess = 0xF01FF;
        private const uint Delete = 0x10000;

        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
        private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint access);

        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr OpenService(IntPtr manager, string serviceName, uint access);

        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DeleteService(IntPtr service);

        [System.Runtime.InteropServices.DllImport("advapi32.dll")]
        private static extern bool CloseServiceHandle(IntPtr handle);

        /// <summary>True si quedó marcada para borrar tras reinicio.</summary>
        public static bool DeleteService(string serviceName)
        {
            var manager = OpenSCManager(null, null, Delete); // DELETE solo para abrir
            if (manager == IntPtr.Zero)
            {
                throw new InvalidOperationException("SCM no disponible.");
            }

            try
            {
                var service = OpenService(manager, serviceName, ServiceAllAccess | Delete);
                if (service == IntPtr.Zero)
                {
                    // Ya no existe: cuenta como éxito de facto.
                    return false;
                }

                try
                {
                    return DeleteService(service);
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(manager);
            }
        }
    }
}

// ---------------- DTOs del puente elevado ----------------

public sealed class UninstallKeyPayload
{
    public string RegistryKeyPath { get; init; } = "";
}

public sealed class OfficialUninstallResultDto
{
    public bool Launched { get; init; }
    public string Launcher { get; init; } = "";
}

public sealed class CleanupPayload
{
    public List<CleanupItemPayload> Items { get; init; } = [];
}

public sealed class CleanupItemPayload
{
    public string Kind { get; init; } = "";
    public string Value { get; init; } = "";
}

public sealed class RemovalOutcomeDto
{
    public List<RemovalItemResultDto> Results { get; init; } = [];
    public bool RequiresRestart { get; init; }
}

public sealed class RemovalItemResultDto
{
    public string Kind { get; init; } = "";
    public string Identifier { get; init; } = "";
    public bool Succeeded { get; init; }
    public string? Detail { get; init; }
}
