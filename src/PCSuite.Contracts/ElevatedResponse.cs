using System.Text.Json;

namespace PCSuite.Contracts;

/// <summary>
/// Único mensaje del proceso elevado → cliente. Mismo esquema que
/// <see cref="BridgeResponse"/> (sin id): success o error controlado.
/// </summary>
public sealed class ElevatedResponse
{
    public bool Success { get; init; }

    public JsonElement? Result { get; init; }

    public BridgeError? Error { get; init; }

    public static ElevatedResponse Ok(JsonElement? result = null) =>
        new() { Success = true, Result = result };

    public static ElevatedResponse Fail(string code, string message) =>
        new() { Success = false, Error = new BridgeError { Code = code, Message = message } };
}
