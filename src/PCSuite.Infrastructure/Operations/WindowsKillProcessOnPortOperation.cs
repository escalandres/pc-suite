using System.Diagnostics;
using PCSuite.Contracts;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

/// <summary>
/// windows.killProcessOnPort: mata los procesos que escuchan en un puerto TCP
/// dado, usando el comando PowerShell
/// <c>Stop-Process -Id (Get-NetTCPConnection -LocalPort {PORT}).OwningProcess -Force</c>.
/// El puerto se valida (1-65535) y se interpola como entero (sin injection).
/// Elevado (UAC por operación): Get-NetTCPConnection y Stop-Process requieren
/// privilegios para procesos de otros usuarios/servicios.
/// </summary>
public sealed class WindowsKillProcessOnPortOperation : IOperation
{
    public string Id => OperationIds.WindowsKillProcessOnPort;
    public bool RequiresElevation => true;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken = default)
    {
        int port = 0;
        if (context.Payload.HasValue && context.Payload.Value.TryGetProperty("port", out var p) && p.TryGetInt32(out var v))
            port = v;
        if (port < 1 || port > 65535)
            return Task.FromResult(OperationResult.Fail("error", "Puerto inválido (1-65535)."));

        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell))
            powershell = "powershell.exe";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"Stop-Process -Id (Get-NetTCPConnection -LocalPort {port} -ErrorAction SilentlyContinue).OwningProcess -Force -ErrorAction SilentlyContinue\"",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
            };
            Process.Start(psi);
            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail("error", $"No se pudo detener el proceso en el puerto {port}: {ex.Message}"));
        }
    }
}