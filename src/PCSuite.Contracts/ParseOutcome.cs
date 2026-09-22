namespace PCSuite.Contracts;

/// <summary>
/// Result of parsing a raw bridge message.
/// Invariant: when <see cref="Error"/> is <c>null</c>, <see cref="Request"/> is not <c>null</c>.
/// <see cref="Id"/> is populated whenever it could be extracted, so a correlation
/// id survives even for rejected messages.
/// </summary>
public sealed class ParseOutcome
{
    public string Id { get; init; } = "";

    public BridgeRequest? Request { get; init; }

    public BridgeError? Error { get; init; }
}
