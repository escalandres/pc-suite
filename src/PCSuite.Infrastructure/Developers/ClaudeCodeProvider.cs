using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.Developers;

/// <summary>Detección de Claude Code (<c>claude --version</c>).</summary>
public sealed class ClaudeCodeProvider : DeveloperToolProviderBase
{
    public ClaudeCodeProvider(IProcessRunner runner, ExecutableLocator locator)
        : base(runner, locator)
    {
    }

    public override string Id => "claude-code";

    public override string Name => "Claude Code";

    protected override string ExecutableName => "claude";
}
