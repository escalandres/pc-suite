namespace PCSuite.Contracts;

/// <summary>Colección tipada devuelta por clamav.history.</summary>
public sealed class ClamAvHistoryResult
{
    /// <summary>Entradas del historial, la más reciente primero.</summary>
    public IReadOnlyList<ClamAvHistoryEntryResult> Entries { get; init; } = [];
}
