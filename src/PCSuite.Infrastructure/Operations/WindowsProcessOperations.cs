using System.Diagnostics;
using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

public sealed class WindowsListProcessesOperation : IOperation
{
    public string Id => OperationIds.WindowsListProcesses;
    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken = default)
    {
        var list = Process.GetProcesses()
            .Select(p =>
            {
                long mem = 0;
                try { mem = p.WorkingSet64; } catch { }
                return new { pid = p.Id, name = p.ProcessName, memory = mem };
            })
            .OrderBy(p => p.name)
            .ToList();
        var json = JsonSerializer.Serialize(list, BridgeJson.Options);
        return Task.FromResult(OperationResult.Ok(JsonDocument.Parse(json).RootElement));
    }
}

public sealed class WindowsKillProcessOperation : IOperation
{
    public string Id => OperationIds.WindowsKillProcess;
    public bool RequiresElevation => true;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken = default)
    {
        int pid = 0;
        if (context.Payload.HasValue && context.Payload.Value.TryGetProperty("pid", out var p) && p.TryGetInt32(out var v))
            pid = v;
        if (pid <= 0)
            return Task.FromResult(OperationResult.Fail("error", "PID inválido."));

        try
        {
            var proc = Process.GetProcessById(pid);
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(5000);
            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail("error", $"No se pudo terminar el proceso: {ex.Message}"));
        }
    }
}
