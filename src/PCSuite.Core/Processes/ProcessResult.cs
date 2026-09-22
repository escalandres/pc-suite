namespace PCSuite.Core.Processes;

public sealed class ProcessResult
{
    public int ExitCode { get; init; }

    public string StandardOutput { get; init; } = "";

    public string StandardError { get; init; } = "";

    public TimeSpan Duration { get; init; }
}
