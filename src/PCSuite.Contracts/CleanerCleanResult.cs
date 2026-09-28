namespace PCSuite.Contracts;

/// <summary>Resultado de cleaner.clean: una fila por categoría solicitada.</summary>
public sealed class CleanerCleanResult
{
    /// <summary>Categorías limpiadas, en el orden pedido.</summary>
    public IReadOnlyList<CleanerCategoryCleanResult> Categories { get; init; } = [];
}
