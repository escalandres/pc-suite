namespace PCSuite.Contracts;

/// <summary>Typed payload returned by the <c>system.info</c> operation.</summary>
public sealed class SystemInfoResult
{
    public string AppVersion { get; init; } = "";

    public string RuntimeVersion { get; init; } = "";

    public string OsDescription { get; init; } = "";

    public string OsVersion { get; init; } = "";

    public string MachineName { get; init; } = "";

    public int ProcessorCount { get; init; }

    public bool Is64BitOperatingSystem { get; init; }

    public bool IsAdministrator { get; init; }
}
