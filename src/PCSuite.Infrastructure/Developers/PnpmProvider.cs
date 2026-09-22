using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.Developers;

/// <summary>Detección de pnpm (<c>pnpm --version</c>).</summary>
public sealed class PnpmProvider : DeveloperToolProviderBase
{
    public PnpmProvider(IProcessRunner runner, ExecutableLocator locator)
        : base(runner, locator)
    {
    }

    public override string Id => "pnpm";

    public override string Name => "pnpm";

    protected override string ExecutableName => "pnpm";
}
