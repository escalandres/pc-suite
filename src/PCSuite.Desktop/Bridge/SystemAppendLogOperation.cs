using PCSuite.Contracts;
using PCSuite.Core.Operations;

namespace PCSuite.Desktop.Bridge;

/// <summary>
/// system.appendLog — anexa un mensaje al log del frontend para diagnóstico.
/// Escribe en %LOCALAPPDATA%\PCSuite\logs\frontend.log (append).
/// </summary>
public sealed class SystemAppendLogOperation : IOperation
{
    public string Id => OperationIds.SystemAppendLog;
    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct = default)
    {
        string message = string.Empty;
        if (context.Payload.HasValue && context.Payload.Value.TryGetProperty("message", out var m))
        {
            message = m.GetString() ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            return Task.FromResult(OperationResult.Ok());
        }

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PCSuite", "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "frontend.log");
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            File.AppendAllText(path, line);
            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail("error", ex.Message));
        }
    }
}