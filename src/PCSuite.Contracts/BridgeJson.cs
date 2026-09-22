using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCSuite.Contracts;

/// <summary>Shared serialization contract for the WebView2 message protocol.</summary>
public static class BridgeJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ParseOutcome ParseRequest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ParseOutcome { Error = Malformed("The message is empty.") };
        }

        BridgeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(json, Options);
        }
        catch (JsonException)
        {
            return new ParseOutcome { Error = Malformed("The message is not valid JSON.") };
        }

        if (request is null)
        {
            return new ParseOutcome { Error = Malformed("The message is empty.") };
        }

        if (string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Operation))
        {
            return new ParseOutcome
            {
                Id = request.Id,
                Request = request,
                Error = Malformed("Both 'id' and 'operation' are required."),
            };
        }

        return new ParseOutcome { Id = request.Id, Request = request };
    }

    public static string SerializeResponse(BridgeResponse response) =>
        JsonSerializer.Serialize(response, Options);

    public static string SerializeEvent(BridgeEvent bridgeEvent) =>
        JsonSerializer.Serialize(bridgeEvent, Options);

    public static JsonElement? ToJson<T>(T value) =>
        value is null ? null : JsonSerializer.SerializeToElement(value, Options);

    private static BridgeError Malformed(string message) =>
        new() { Code = BridgeErrorCodes.MalformedMessage, Message = message };
}
