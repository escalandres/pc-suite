using System.Text.Json.Serialization;

namespace PCSuite.Contracts;

public sealed class FilesStegoEmbedPayload
{
    public string? ImagePath { get; init; }
    public string? TextPayload { get; init; }
    public string? FilePayloadPath { get; init; }
    public string? Password { get; init; }
    public string? OutputPath { get; init; }
    public bool? DeleteOriginal { get; init; }
}

public sealed class FilesStegoVerifyPayload
{
    public string? ImagePath { get; init; }
}

public sealed class FilesStegoExtractPayload
{
    public string? ImagePath { get; init; }
    public string? Password { get; init; }
    public string? OutputPath { get; init; }
}
