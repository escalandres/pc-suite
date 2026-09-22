using System.Text.Json;

namespace PCSuite.Core.Tools;

/// <summary>
/// Resultado de ejecutar una tool. Los errores son mensajes controlados
/// aptos para la UI: nunca contienen stack traces.
/// </summary>
public sealed class ToolResult
{
    public bool Success { get; init; }

    public JsonElement? Output { get; init; }

    public string? ErrorMessage { get; init; }

    public static ToolResult Ok(JsonElement? output = null) =>
        new() { Success = true, Output = output };

    public static ToolResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
