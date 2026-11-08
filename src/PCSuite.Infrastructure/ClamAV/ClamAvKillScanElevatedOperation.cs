using System.Diagnostics;
using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.ClamAV;

/// <summary>
/// Operación elevada que termina un escaneo de ClamAV (Task 025.3-full).
/// El proceso padre (no elevado) no puede matar un clamscan elevado (Access
/// Denied), así que el kill se ejecuta DENTRO de un proceso elevado: aquí
/// sí tenemos permisos para terminar el árbol del proceso.
///
/// Seguridad en profundidad:
/// - ID allow-listed (<see cref="ElevatedOperationIds.ClamavKillScan"/>).
/// - El payload solo aporta un PID (entero); se revalida que sea &gt; 0 y que
///   el proceso vivo sea realmente clamscan.exe antes de matarlo (nunca se
///   mata un PID arbitrario).
/// - Jamás ejecuta un shell ni acepta rutas/comandos del payload.
/// </summary>
public sealed class ClamAvKillScanElevatedOperation : IElevatedOperation
{
    public string Id => ElevatedOperationIds.ClamavKillScan;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(OperationResult.Fail(BridgeErrorCodes.NotSupported, "Solo disponible en Windows."));
        }

        int pid;
        try
        {
            if (context.Payload is null || !context.Payload.Value.TryGetProperty("pid", out var pidEl))
            {
                return Task.FromResult(OperationResult.Fail(BridgeErrorCodes.OperationFailed, "Falta el pid."));
            }
            pid = pidEl.GetInt32();
        }
        catch
        {
            return Task.FromResult(OperationResult.Fail(BridgeErrorCodes.OperationFailed, "pid inválido."));
        }

        if (pid <= 0)
        {
            return Task.FromResult(OperationResult.Fail(BridgeErrorCodes.OperationFailed, "pid inválido."));
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            // Revalidar que sea clamscan: jamás matar un PID arbitrario.
            if (!string.Equals(process.ProcessName, "clamscan", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(OperationResult.Fail(
                    BridgeErrorCodes.OperationFailed,
                    "El proceso indicado no es clamscan."));
            }

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Ya terminó: éxito idempotente.
            }
            try
            {
                process.WaitForExit(5_000);
            }
            catch (InvalidOperationException)
            {
            }

            return Task.FromResult(OperationResult.Ok());
        }
        catch (ArgumentException)
        {
            // Proceso inexistente: tratar como éxito idempotente.
            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception)
        {
            return Task.FromResult(OperationResult.Fail(BridgeErrorCodes.OperationFailed, "No se pudo terminar el escaneo."));
        }
    }
}