using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.Developers;

/// <summary>Detección de Node.js (<c>node --version</c>).</summary>
public sealed class NodeProvider : DeveloperToolProviderBase
{
    public NodeProvider(IProcessRunner runner, ExecutableLocator locator)
        : base(runner, locator)
    {
    }

    public override string Id => "node";

    public override string Name => "Node.js";

    protected override string ExecutableName => "node";
}
