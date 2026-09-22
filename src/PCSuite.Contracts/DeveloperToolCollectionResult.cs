namespace PCSuite.Contracts;

/// <summary>Colección de tools devuelta por developer.list.</summary>
public sealed class DeveloperToolCollectionResult
{
    public IReadOnlyList<DeveloperToolInfoResult> Tools { get; init; } = [];
}
