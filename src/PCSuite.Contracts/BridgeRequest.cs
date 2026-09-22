using System.Text.Json;

namespace PCSuite.Contracts;

/// <summary>Request message sent from the frontend to the native layer.</summary>
public sealed class BridgeRequest
{
    public string Id { get; init; } = "";

    public string Operation { get; init; } = "";

    public JsonElement? Payload { get; init; }
}
