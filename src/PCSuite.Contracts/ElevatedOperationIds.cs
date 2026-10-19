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

    // freshclam (Task 008): la ÚNICA mutación ClamAV que usa este helper
    // (escribir las definiciones en Program Files). Los escaneos corren
    // elevados desde 2026-10-18 por runas DIRECTO de clamscan con --log
    // temporal (tail en vivo) — no pasan por este helper. Nunca un shell.
    public const string ClamavUpdateDefinitions = "elevated.clamav.updateDefinitions";

    // Regla de firewall de Local Share (Task 016.6): la ÚNICA mutación de
    // firewall. El payload solo aporta el puerto, que se REVALIDA (entero
    // 1-65535); el nombre de regla y la ruta del programa son fijos y se
    // derivan del propio helper (jamás del payload). Nunca un shell.
    public const string LocalShareFirewallRule = "elevated.localshare.firewallRule";

    // Desinstalador forzado (Task 020): el payload porta la clave de registro
    // o los ítems aprobados, pero el proceso elevado RELEE el registro y
    // REVALIDA cada ítem contra reglas duras (rutas absolutas, jamás raíces
    // del sistema, servicios por contención de ruta). El comando jamás viaja
    // por el puente: se relee de la entrada de la propia app.
    public const string UninstallRunOfficial = "elevated.uninstall.runOfficial";

    public const string UninstallCleanup = "elevated.uninstall.cleanup";

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
        ClamavUpdateDefinitions,
        LocalShareFirewallRule,
        UninstallRunOfficial,
        UninstallCleanup,
    };
}
