namespace PCSuite.Contracts;

/// <summary>Resultado de cleaner.scan: una fila por categoría fija.</summary>
public sealed class CleanerScanResult
{
    /// <summary>Categorías escaneadas (siempre la lista completa y en orden fijo).</summary>
    public IReadOnlyList<CleanerCategoryScanResult> Categories { get; init; } = [];
}
