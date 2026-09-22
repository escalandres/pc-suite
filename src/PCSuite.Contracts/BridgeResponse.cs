using System.Text.Json;

namespace PCSuite.Contracts;

/// <summary>Response message sent from the native layer back to the frontend.</summary>
public sealed class BridgeResponse
{
    public string Id { get; init; } = "";

    public bool Success { get; init; }

    public JsonElement? Result { get; init; }

    public BridgeError? Error { get; init; }
}
