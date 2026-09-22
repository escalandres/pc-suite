using System.Text.Json;

namespace PCSuite.Core.Elevation;

/// <summary>
/// Resultado de <see cref="IElevationManager.RunElevatedAsync"/>. Mismo
/// espíritu que <c>OperationResult</c>: código + mensaje controlados, aptos
/// para la UI (nunca stack traces ni detalles internos).
/// </summary>
public sealed class ElevationResult
{
    public bool Success { get; init; }

    public JsonElement? Data { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static ElevationResult Ok(JsonElement? data = null) =>
        new() { Success = true, Data = data };

    public static ElevationResult Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, ErrorMessage = message };
}
