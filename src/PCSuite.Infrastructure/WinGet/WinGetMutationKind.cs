namespace PCSuite.Infrastructure.WinGet;

/// <summary>
/// Mutación winget permitida dentro del proceso elevado. Una instancia de
/// <c>WinGetElevatedOperation</c> por kind, cada una con su id allow-listed.
/// </summary>
public enum WinGetMutationKind
{
    Install,
    Upgrade,
    UpgradeAll,
    Uninstall,
}
