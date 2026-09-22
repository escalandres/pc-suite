using PCSuite.Contracts;
using PCSuite.Core.Developers;
using PCSuite.Core.Operations;
using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.Developers;

/// <summary>
/// Implementación común de los proveedores de herramientas de desarrollador.
///
/// Detecta el ejecutable en la PATH y obtiene la versión ejecutando
/// <c>--version</c>; comprueba actualizaciones contra la fuente declarada por
/// cada proveedor; se autoactualiza cuando la herramienta lo permite; y la
/// lanza en su propia ventana de consola cuando tiene interfaz interactiva.
/// Sin shell en ningún caso: todo va por <see cref="IProcessRunner"/> con los
/// argumentos en lista.
/// </summary>
public abstract class DeveloperToolProviderBase : IDeveloperToolProvider
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Ventana de sondeo del lanzamiento: si la herramienta sigue viva tras
    /// este tiempo, se considera lanzada. Permite reportar fallos de arranque
    /// inmediatos sin esperar a que el usuario cierre la herramienta.
    /// </summary>
    private static readonly TimeSpan LaunchProbe = TimeSpan.FromMilliseconds(500);

    /// <summary>Máximo de caracteres de detalle de fallo aptos para la UI.</summary>
    private const int MaxDetailLength = 300;

    private readonly IProcessRunner _runner;
    private readonly ExecutableLocator _locator;

    protected DeveloperToolProviderBase(IProcessRunner runner, ExecutableLocator locator)
    {
        _runner = runner;
        _locator = locator;
    }

    public abstract string Id { get; }

    public abstract string Name { get; }

    /// <summary>Nombre base del ejecutable a localizar ("node", "npm"...).</summary>
    protected abstract string ExecutableName { get; }

    /// <summary>
    /// Comando de solo lectura que imprime la última versión publicada. Se
    /// resuelve contra la PATH y puede apuntar a otra herramienta (p. ej. la
    /// consulta al registro npm vía npm). Null: sin fuente fiable y
    /// <see cref="CheckUpdateAsync"/> degrada a null.
    /// </summary>
    protected virtual ProcessRequest? LatestVersionRequest => null;

    /// <summary>
    /// Comando de autoactualización con timeout de descarga. Null:
    /// <see cref="UpdateAsync"/> devuelve un fallo controlado NOT_SUPPORTED.
    /// </summary>
    protected virtual ProcessRequest? SelfUpdateRequest => null;

    /// <summary>Motivo controlado cuando no hay <see cref="SelfUpdateRequest"/>.</summary>
    protected virtual string UpdateNotSupportedMessage =>
        $"La actualización de {Name} aún no está disponible.";

    /// <summary>
    /// True si la herramienta tiene interfaz interactiva propia (REPL/TUI) y
    /// <see cref="LaunchAsync"/> puede abrirla en una ventana de consola.
    /// </summary>
    protected virtual bool SupportsLaunch => false;

    /// <summary>Motivo controlado cuando <see cref="SupportsLaunch"/> es false.</summary>
    protected virtual string LaunchNotSupportedMessage =>
        $"La herramienta {Name} no tiene interfaz interactiva propia.";

    public async Task<ToolStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var path = _locator.Find(ExecutableName);
        if (path is null)
        {
            return new ToolStatus { IsInstalled = false };
        }

        var version = await GetVersionAsync(path, cancellationToken).ConfigureAwait(false);
        return new ToolStatus { IsInstalled = true, Version = version, Path = path };
    }

    public async Task<UpdateInfo?> CheckUpdateAsync(CancellationToken cancellationToken)
    {
        var template = LatestVersionRequest;
        if (template is null)
        {
            return null;
        }

        var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsInstalled || status.Version is null)
        {
            return null;
        }

        var latest = await ExecuteSourceAsync(template, cancellationToken).ConfigureAwait(false);
        if (latest is null)
        {
            return null;
        }

        return new UpdateInfo
        {
            CurrentVersion = status.Version,
            LatestVersion = latest,
            UpdateAvailable = IsNewer(latest, status.Version),
        };
    }

    public async Task<OperationResult> UpdateAsync(CancellationToken cancellationToken)
    {
        var template = SelfUpdateRequest;
        if (template is null)
        {
            return OperationResult.Fail("NOT_SUPPORTED", UpdateNotSupportedMessage);
        }

        var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsInstalled)
        {
            return OperationResult.Fail("NOT_INSTALLED", $"{Name} no está instalada.");
        }

        var path = _locator.Find(template.FileName);
        if (path is null)
        {
            return OperationResult.Fail("NOT_INSTALLED", $"{Name} no está instalada.");
        }

        try
        {
            var result = await _runner
                .ExecuteAsync(template with { FileName = path }, cancellationToken)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                var detail = FirstMeaningfulLine(result.StandardError, result.StandardOutput);
                return SuggestsElevation(result)
                    ? OperationResult.Fail(
                        "ELEVATION_REQUIRED",
                        WithDetail(
                            $"No se pudo actualizar {Name}: puede requerir permisos de administrador " +
                            "(la elevación llega con la Task 005).",
                            detail))
                    : OperationResult.Fail(
                        "UPDATE_FAILED",
                        WithDetail(
                            $"No se pudo actualizar {Name} (código de salida {result.ExitCode}).",
                            detail));
            }

            var after = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(BridgeJson.ToJson(new
            {
                previousVersion = status.Version,
                currentVersion = after.Version,
            }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProcessTimeoutException ex)
        {
            // Mensaje controlado del propio tipo: nunca contiene stack trace.
            return OperationResult.Fail("UPDATE_TIMEOUT", ex.Message);
        }
        catch (Exception)
        {
            return OperationResult.Fail("UPDATE_FAILED", $"No se pudo actualizar {Name}.");
        }
    }

    public async Task<OperationResult> LaunchAsync(CancellationToken cancellationToken)
    {
        if (!SupportsLaunch)
        {
            return OperationResult.Fail("NOT_SUPPORTED", LaunchNotSupportedMessage);
        }

        var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsInstalled || status.Path is null)
        {
            return OperationResult.Fail("NOT_INSTALLED", $"{Name} no está instalada.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        Task<ProcessResult> launchTask;
        try
        {
            launchTask = _runner.ExecuteAsync(
                new ProcessRequest { FileName = status.Path, Interactive = true },
                CancellationToken.None);
        }
        catch (Exception)
        {
            // El runner no es async en algunos extremos (fakes): fallo controlado.
            return OperationResult.Fail("LAUNCH_FAILED", $"No se pudo lanzar {Name}.");
        }

        // Lanzamiento en segundo plano: la herramienta vive hasta que el
        // usuario cierre su ventana; cualquier fallo tardío se observa aquí
        // para que no quede una tarea sin registrar.
        _ = launchTask.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // Sondeo corto: reporta fallos de arranque inmediatos; si la
        // herramienta sigue viva, se devuelve el éxito sin esperar su cierre.
        var finished = await Task
            .WhenAny(launchTask, Task.Delay(LaunchProbe, cancellationToken))
            .ConfigureAwait(false);

        if (finished != launchTask)
        {
            return OperationResult.Ok();
        }

        if (launchTask.IsFaulted)
        {
            return OperationResult.Fail("LAUNCH_FAILED", $"No se pudo lanzar {Name}.");
        }

        return launchTask.Result.ExitCode == 0
            ? OperationResult.Ok()
            : OperationResult.Fail(
                "LAUNCH_FAILED",
                $"No se pudo lanzar {Name} (código de salida {launchTask.Result.ExitCode}).");
    }

    /// <summary>
    /// Ejecuta el comando de consulta de última versión (resolviendo su
    /// ejecutable en la PATH) y devuelve la versión impresa, o null si la
    /// fuente no está disponible o la consulta falla (comprobación degradada).
    /// </summary>
    private async Task<string?> ExecuteSourceAsync(ProcessRequest template, CancellationToken cancellationToken)
    {
        var path = _locator.Find(template.FileName);
        if (path is null)
        {
            return null;
        }

        try
        {
            var result = await _runner
                .ExecuteAsync(template with { FileName = path }, cancellationToken)
                .ConfigureAwait(false);
            return result.ExitCode == 0 ? ParseVersion(result.StandardOutput) : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Sin red, sin registro o timeout: la comprobación degrada a null.
            return null;
        }
    }

    /// <summary>Comando de consulta de última versión (timeout generoso de red).</summary>
    protected static ProcessRequest LatestVersion(string fileName, params string[] arguments) => new()
    {
        FileName = fileName,
        Arguments = arguments,
        Timeout = TimeSpan.FromSeconds(60),
    };

    /// <summary>Comando de autoactualización (timeout de descarga e instalación).</summary>
    protected static ProcessRequest SelfUpdate(string fileName, params string[] arguments) => new()
    {
        FileName = fileName,
        Arguments = arguments,
        Timeout = TimeSpan.FromMinutes(5),
    };

    private async Task<string?> GetVersionAsync(string path, CancellationToken cancellationToken)
    {
        var request = new ProcessRequest
        {
            FileName = path,
            Arguments = ["--version"],
            Timeout = VersionTimeout,
        };

        try
        {
            var result = await _runner.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 ? ParseVersion(result.StandardOutput) : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Timeout o ejecución imposible: sigue instalada, versión desconocida.
            return null;
        }
    }

    private static string? ParseVersion(string output)
    {
        var line = output
            .Split('\n')
            .Select(part => part.Trim())
            .FirstOrDefault(part => part.Length > 0);

        if (line is null)
        {
            return null;
        }

        // "2.1.181 (Claude Code)" → primera palabra = versión.
        var version = line.Split(' ', '\t')[0];
        if (version.Length == 0 || version.Length > 64)
        {
            return null;
        }

        if (version[0] is 'v' or 'V')
        {
            version = version[1..];
        }

        return version.Length > 0 ? version : null;
    }

    /// <summary>True si <paramref name="latest"/> es mayor que <paramref name="current"/>.</summary>
    private static bool IsNewer(string latest, string current) =>
        TryParseVersion(latest, out var parsedLatest) && TryParseVersion(current, out var parsedCurrent)
            ? parsedLatest > parsedCurrent
            : !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);

    private static bool TryParseVersion(string value, out Version version)
    {
        version = new Version(0, 0);

        var core = value.Trim();
        if (core.Length == 0)
        {
            return false;
        }

        if (core[0] is 'v' or 'V')
        {
            core = core[1..];
        }

        // "2.1.181-beta" → solo el tramo numérico (los prerelease no comparan).
        var suffix = core.IndexOfAny(['-', '+']);
        if (suffix >= 0)
        {
            core = core[..suffix];
        }

        if (!Version.TryParse(core, out var parsed) || parsed is null)
        {
            return false;
        }

        version = parsed;
        return true;
    }

    /// <summary>
    /// Primera línea útil de la salida de un fallo: sin líneas vacías ni
    /// frames de stack, truncada a <see cref="MaxDetailLength"/> caracteres.
    /// Apta para la UI (nunca contiene stack traces).
    /// </summary>
    private static string? FirstMeaningfulLine(params string[] outputs)
    {
        foreach (var output in outputs)
        {
            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                // Prefijos habituales de gestores de paquetes ("npm ERR!").
                var normalized = line;
                while (true)
                {
                    if (normalized.StartsWith("npm ERR!", StringComparison.OrdinalIgnoreCase))
                    {
                        normalized = normalized["npm ERR!".Length..].TrimStart();
                    }
                    else if (normalized.StartsWith("ERR!", StringComparison.OrdinalIgnoreCase))
                    {
                        normalized = normalized["ERR!".Length..].TrimStart();
                    }
                    else
                    {
                        break;
                    }
                }

                if (normalized.StartsWith("at ", StringComparison.Ordinal))
                {
                    continue; // frame de stack
                }

                return line.Length <= MaxDetailLength ? line : line[..MaxDetailLength] + "…";
            }
        }

        return null;
    }

    /// <summary>
    /// La salida sugiere que la operación necesita permisos elevados
    /// (EACCES/EPERM/denegación de acceso en Windows o POSIX).
    /// </summary>
    private static bool SuggestsElevation(ProcessResult result)
    {
        var text = $"{result.StandardError}\n{result.StandardOutput}";
        return text.Contains("EACCES", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("EPERM", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("permission denied", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("access is denied", StringComparison.OrdinalIgnoreCase);
    }

    private static string WithDetail(string message, string? detail) =>
        string.IsNullOrEmpty(detail) ? message : $"{message} Detalle: {detail}";
}
