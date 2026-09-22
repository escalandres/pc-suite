using System.Text.Json;

namespace PCSuite.Core.Operations;

public sealed class OperationResult
{
    public bool Success { get; init; }

    public JsonElement? Data { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static OperationResult Ok(JsonElement? data = null) =>
        new() { Success = true, Data = data };

    public static OperationResult Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, ErrorMessage = message };
}
