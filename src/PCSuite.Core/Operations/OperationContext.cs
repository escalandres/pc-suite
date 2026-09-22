using System.Text.Json;

namespace PCSuite.Core.Operations;

public sealed class OperationContext
{
    public JsonElement? Payload { get; init; }
}
