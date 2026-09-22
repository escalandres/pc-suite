using PCSuite.Application.Tools;
using PCSuite.Core.Tools;

namespace PCSuite.Application.Tests;

public class ToolSearchServiceTests
{
    [Fact]
    public void Search_EmptyQuery_ReturnsAllToolsOrderedById()
    {
        var service = CreateService(
            new StubTool("json.format", "JSON Formatter", "Formatea JSON", ["json"]),
            new StubTool("base64", "Base64", "Codifica texto", ["encode"]),
            new StubTool("hash", "Hash", "Calcula hashes", ["sha256"]));

        Assert.Equal(
            ["base64", "hash", "json.format"],
            service.Search("").Select(t => t.Metadata.Id).ToArray());
        Assert.Equal(
            ["base64", "hash", "json.format"],
            service.Search("   ").Select(t => t.Metadata.Id).ToArray());
        Assert.Equal(
            ["base64", "hash", "json.format"],
            service.Search(null).Select(t => t.Metadata.Id).ToArray());
    }

    [Fact]
    public void Search_MatchesByName_CaseInsensitive()
    {
        var service = CreateService(
            new StubTool("json.format", "JSON Formatter", "Formatea JSON", ["json"]),
            new StubTool("hash", "Hash", "Calcula hashes", ["sha256"]));

        var results = service.Search("formatter");

        var match = Assert.Single(results);
        Assert.Equal("json.format", match.Metadata.Id);
    }

    [Fact]
    public void Search_MatchesByKeyword()
    {
        var service = CreateService(
            new StubTool("uuid.generate", "UUID Generator", "Genera UUIDs", ["guid", "random"]),
            new StubTool("hash", "Hash", "Calcula hashes", ["sha256"]));

        Assert.Equal("uuid.generate", Assert.Single(service.Search("GUID")).Metadata.Id);
    }

    [Fact]
    public void Search_ExactIdOutranksPartialNameMatch()
    {
        var service = CreateService(
            new StubTool("hash", "Hash Generator", "Genera hashes", []),
            new StubTool("hash.tool", "Otra", "Otra tool", []));

        // "hash" es id exacto de la primera (100) y solo substring de la segunda (80).
        var results = service.Search("hash");

        Assert.Equal(2, results.Count);
        Assert.Equal("hash", results[0].Metadata.Id);
    }

    [Fact]
    public void Search_MultiTerm_RequiresAllTerms()
    {
        var service = CreateService(
            new StubTool("gen.json", "Generador JSON", "Genera JSON formateado", ["genera", "json"]),
            new StubTool("gen.base64", "Generador Base64", "Genera Base64", ["genera", "base64"]),
            new StubTool("hash", "Hash", "Calcula hashes", ["sha256"]));

        var results = service.Search("genera json");

        // AND: solo "gen.json" contiene ambos términos; las demás quedan fuera
        // aunque coincidan con uno de ellos ("gen.base64" tiene "genera").
        var match = Assert.Single(results);
        Assert.Equal("gen.json", match.Metadata.Id);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var service = CreateService(new StubTool("hash", "Hash", "Calcula hashes", ["sha256"]));

        Assert.Empty(service.Search("zzz-no-existe"));
    }

    [Fact]
    public void Search_NullQueryOnEmptyRegistry_ReturnsEmpty()
    {
        var service = new ToolSearchService(new ToolRegistry());

        Assert.Empty(service.Search(null));
    }

    private static ToolSearchService CreateService(params StubTool[] tools)
    {
        var registry = new ToolRegistry();
        registry.RegisterModule(new StubModule(tools));
        return new ToolSearchService(registry);
    }

    private sealed class StubModule(StubTool[] tools) : IModule
    {
        public string Id => "stub";

        public string Name => "Stub";

        public IReadOnlyCollection<ITool> Tools => tools;
    }

    private sealed class StubTool(string id, string name, string description, string[] keywords) : ITool
    {
        public ToolMetadata Metadata { get; } = new()
        {
            Id = id,
            ModuleId = "stub",
            Name = name,
            Description = description,
            Keywords = keywords,
        };

        public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(ToolResult.Ok());
    }
}
