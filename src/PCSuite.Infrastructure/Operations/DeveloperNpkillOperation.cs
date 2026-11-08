using PCSuite.Contracts;
using PCSuite.Core.Operations;
using System.Diagnostics;

namespace PCSuite.Infrastructure.Operations;

/// <summary>
/// developer.npkill: lanza npkill en una consola elevada para liberar espacio
/// en node_modules. El path se valida (debe existir) y se pasa como argumento
/// fijo — no hay shell arbitrario.
/// </summary>
public sealed class DeveloperNpkillOperation : IOperation
{
    public string Id => OperationIds.DeveloperNpkill;
    public bool RequiresElevation => true;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken = default)
    {
        string? path = null;
        if (context.Payload.HasValue && context.Payload.Value.TryGetProperty("path", out var p))
            path = p.GetString();

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return Task.FromResult(OperationResult.Fail("error", "La ruta no existe o es inválida."));

        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell))
            powershell = "powershell.exe";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = $"-NoProfile -NoExit -ExecutionPolicy Bypass -Command \"npx npkill --directory '{path}'\"",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = path,
            };
            Process.Start(psi);
            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail("error", $"No se pudo lanzar npkill: {ex.Message}"));
        }
    }
}
