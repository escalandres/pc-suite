namespace PCSuite.Contracts;

/// <summary>Colección tipada devuelta por winget.search/list/updates.</summary>
public sealed class WinGetPackageCollectionResult
{
    public IReadOnlyList<WinGetPackageResult> Packages { get; init; } = [];
}
