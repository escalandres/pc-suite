namespace PCSuite.Contracts;

/// <summary>Colección tipada devuelta por wsl.list.</summary>
public sealed class WslListResult
{
    public IReadOnlyList<WslDistroResult> Distributions { get; init; } = [];
}
