namespace PCSuite.Contracts;

/// <summary>Colección de tools devuelta por tools.list y tools.search.</summary>
public sealed class ToolCollectionResult
{
    public IReadOnlyList<ToolInfoResult> Tools { get; init; } = [];
}
