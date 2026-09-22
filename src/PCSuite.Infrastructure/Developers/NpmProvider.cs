using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.Developers;

/// <summary>Detección de npm (<c>npm --version</c>).</summary>
public sealed class NpmProvider : DeveloperToolProviderBase
{
    public NpmProvider(IProcessRunner runner, ExecutableLocator locator)
        : base(runner, locator)
    {
    }

    public override string Id => "npm";

    public override string Name => "npm";

    protected override string ExecutableName => "npm";
}
