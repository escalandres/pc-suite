namespace PCSuite.Contracts;

/// <summary>
/// Allow-list de operaciones elevadas. El proceso NO elevado valida contra
/// esta lista ANTES de pedir UAC (ID fuera de la lista = cero prompts) y el
/// proceso elevado solo admite operaciones cuyo ID figura aquí: dos
/// barreras independientes sobre la misma lista. Tampoco se expone nunca un
/// shell genérico ni ejecución arbitraria.
/// </summary>
public static class ElevatedOperationIds
{
    /// <summary>Operación PoC de solo lectura: datos de la cuenta/proceso elevado.</summary>
    public const string AdminInfo = "elevated.adminInfo";

    // Mutaciones de winget (Task 006): solo se elevan si la ejecución sin
    // privilegios falla por permisos (dos barreras: allow-list + revalidación
    // de packageId dentro del propio proceso elevado). Nunca un shell.
    public const string WingetInstall = "elevated.winget.install";

    public const string WingetUpgrade = "elevated.winget.upgrade";

    public const string WingetUpgradeAll = "elevated.winget.upgradeAll";

    public const string WingetUninstall = "elevated.winget.uninstall";

    // wsl --update (Task 007): la ÚNICA mutación wsl que puede necesitar
    // privilegios. argv fijos dentro del proceso elevado y payload ignorado;
    // shutdown/start jamás elevan. Nunca un shell.
    public const string WslUpdate = "elevated.wsl.update";

    /// <summary>
    /// IDs permitidos. Añadir un ID aquí solo habilita poder registrarlo;
    /// no registra nada por sí mismo.
    /// </summary>
    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        AdminInfo,
        WingetInstall,
        WingetUpgrade,
        WingetUpgradeAll,
        WingetUninstall,
        WslUpdate,
    };
}
