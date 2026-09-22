using PCSuite.Core.Developers;
using PCSuite.Core.Operations;
using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.Developers;

/// <summary>
/// Implementación común de los proveedores de herramientas de desarrollador:
/// detecta el ejecutable en la PATH y obtiene la versión ejecutando
/// <c>--version</c> por el <see cref="IProcessRunner"/> (sin shell). Las
/// capacidades no implementadas en la Task 004 devuelven fallos controlados.
/// </summary>
public abstract class DeveloperToolProviderBase : IDeveloperToolProvider
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(10);

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

    // Task 004: detección y versión primero; las actualizaciones llegan después.
    public Task<UpdateInfo?> CheckUpdateAsync(CancellationToken cancellationToken) =>
        Task.FromResult<UpdateInfo?>(null);

    public Task<OperationResult> UpdateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Fail(
            "NOT_SUPPORTED",
            $"La actualización de {Name} aún no está disponible."));

    public Task<OperationResult> LaunchAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Fail(
            "NOT_SUPPORTED",
            $"El lanzamiento de {Name} aún no está disponible."));

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
}
