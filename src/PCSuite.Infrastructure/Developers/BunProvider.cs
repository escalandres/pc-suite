using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.Developers;

/// <summary>Detección de Bun (<c>bun --version</c>).</summary>
public sealed class BunProvider : DeveloperToolProviderBase
{
    public BunProvider(IProcessRunner runner, ExecutableLocator locator)
        : base(runner, locator)
    {
    }

    public override string Id => "bun";

    public override string Name => "Bun";

    protected override string ExecutableName => "bun";
}
