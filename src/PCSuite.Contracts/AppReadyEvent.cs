namespace PCSuite.Contracts;

/// <summary>Payload of the <see cref="EventIds.AppReady"/> event.</summary>
public sealed class AppReadyEvent
{
    public string AppVersion { get; init; } = "";

    public int ProtocolVersion { get; init; } = 1;

    public DateTimeOffset Timestamp { get; init; }
}
