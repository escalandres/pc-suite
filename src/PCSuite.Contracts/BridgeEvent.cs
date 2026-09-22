using System.Text.Json;

namespace PCSuite.Contracts;

/// <summary>Event message pushed from the native layer to the frontend.</summary>
public sealed class BridgeEvent
{
    public string Event { get; init; } = "";

    public JsonElement? Payload { get; init; }
}
