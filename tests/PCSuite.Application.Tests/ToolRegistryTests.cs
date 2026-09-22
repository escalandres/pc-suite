using PCSuite.Application.Tools;
using PCSuite.Core.Tools;

namespace PCSuite.Application.Tests;

public class ToolRegistryTests
{
    [Fact]
    public void RegisterModule_ExposesModuleAndTools()
    {
        var registry = new ToolRegistry();

        registry.RegisterModule(new StubModule("mod", new StubTool("mod.tool")));

        Assert.Single(registry.Modules);
        Assert.Single(registry.Tools);
        Assert.NotNull(registry.GetTool("mod.tool"));
    }

    [Fact]
    public void GetTool_UnknownId_ReturnsNull()
    {
        var registry = new ToolRegistry();

        Assert.Null(registry.GetTool("nope"));
        Assert.Null(registry.GetTool(null));
        Assert.Null(registry.GetTool(""));
    }

    [Fact]
    public void RegisterModule_DuplicateModuleId_Throws()
    {
        var registry = new ToolRegistry();
        registry.RegisterModule(new StubModule("mod", new StubTool("a.tool")));

        Assert.Throws<InvalidOperationException>(
            () => registry.RegisterModule(new StubModule("mod", new StubTool("b.tool"))));
    }

    [Fact]
    public void RegisterModule_DuplicateToolId_ThrowsAndLeavesRegistryUnchanged()
    {
        var registry = new ToolRegistry();
        registry.RegisterModule(new StubModule("mod-1", new StubTool("same.tool")));

        // El módulo duplicado falla completo: el registro no queda a medias.
        Assert.Throws<InvalidOperationException>(
            () => registry.RegisterModule(new StubModule("mod-2", new StubTool("same.tool"))));

        Assert.Single(registry.Modules);
        Assert.Single(registry.Tools);
    }

    private sealed class StubModule(string id, params ITool[] tools) : IModule
    {
        public string Id => id;

        public string Name => id;

        public IReadOnlyCollection<ITool> Tools => tools;
    }

    private sealed class StubTool(string id) : ITool
    {
        public ToolMetadata Metadata { get; } = new()
        {
            Id = id,
            ModuleId = "stub",
            Name = id,
            Description = "stub tool",
        };

        public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(ToolResult.Ok());
    }
}
