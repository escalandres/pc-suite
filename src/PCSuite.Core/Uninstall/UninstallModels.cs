namespace PCSuite.Core.Uninstall;

/// <summary>
/// App instalada según el registro de desinstalación de Windows (la misma
/// fuente que "Aplicaciones y características" / appwiz.cpl). Cubre lo que
/// winget no ve: portable instalados por instaladores propios, heredados y
/// mal registrados.
/// </summary>
public sealed class InstalledAppInfo
{
    /// <summary>Identificador estable: la ruta completa de la clave de registro
    /// (p. ej. HKEY_LOCAL_MACHINE\SOFTWARE\...\Uninstall\{GUID}). Se valida
    /// contra raíces fijas en cada uso.</summary>
    public required string RegistryKeyPath { get; init; }

    public required string DisplayName { get; init; }

    public string Publisher { get; init; } = "";

    public string DisplayVersion { get; init; } = "";

    /// <summary>Carpeta de instalación declarada por la app (puede estar vacía).</summary>
    public string InstallLocation { get; init; } = "";

    /// <summary>Comando de desinstalación registrado por la PROPIA app
    /// (appwiz ejecuta esto mismo). Puede estar vacío: app rota.</summary>
    public string UninstallString { get; init; } = "";

    /// <summary>GUID de MSI si el instalador fue MSI (desinstalación limpia
    /// vía msiexec /x). Null si no.</summary>
    public string? MsiGuid { get; init; }

    /// <summary>Tamaño estimado en KB (0 si el instalador no lo declaró).</summary>
    public long EstimatedSizeKb { get; init; }

    /// <summary>¿Es un componente del sistema (oculto en appwiz)? Se lista
    /// pero marcado: borrar componentes del sistema es responsabilidad del
    /// usuario y va con advertencia extra.</summary>
    public bool IsSystemComponent { get; init; }
}

/// <summary>Categoría de un elemento del plan de eliminación.</summary>
public enum RemovalItemKind
{
    /// <summary>Proceso vivo cuyo ejecutable vive dentro de la carpeta de la app.</summary>
    Process,

    /// <summary>Servicio cuyo ImagePath apunta dentro de la carpeta de la app.</summary>
    Service,

    /// <summary>Tarea programada que ejecuta un binario de la carpeta de la app.</summary>
    ScheduledTask,

    /// <summary>Carpeta a eliminar (instalación o residuo de datos).</summary>
    Folder,

    /// <summary>Clave de registro (entrada de desinstalación o residuo).</summary>
    RegistryKey,
}

/// <summary>
/// Certeza del ítem: <see cref="Derived"/> viene del registro de la propia
/// app o de contención de rutas (quíirurgico, pre-marcado);
/// <see cref="Candidate"/> es heurístico (carpeta de datos con el nombre de
/// la app) y se ofrece SIN marcar — el usuario decide.
/// </summary>
public enum RemovalItemTier
{
    Derived,
    Candidate,
}

/// <summary>Un elemento concreto del plan de eliminación.</summary>
public sealed class RemovalPlanItem
{
    public required RemovalItemKind Kind { get; init; }

    public required RemovalItemTier Tier { get; init; }

    /// <summary>Identificador según Kind: pid:name del proceso, nombre del
    /// servicio, ruta de la tarea, ruta de carpeta o ruta de clave.</summary>
    public required string Identifier { get; init; }

    /// <summary>Descripción legible para la UI (ej. "C:\Program Files\App\svc.exe").</summary>
    public string Detail { get; init; } = "";
}

/// <summary>Plan de eliminación revisable por el usuario.</summary>
public sealed class RemovalPlan
{
    public required InstalledAppInfo App { get; init; }

    /// <summary>¿Existe desinstalador oficial (UninstallString o MSI)? El
    /// camino preferente: ejecutarlo ANTES de la limpieza manual.</summary>
    public bool HasOfficialUninstaller { get; init; }

    public IReadOnlyList<RemovalPlanItem> Items { get; init; } = [];

    /// <summary>Aviso cuando la app es un componente del sistema.</summary>
    public bool IsSystemComponent => App.IsSystemComponent;
}

/// <summary>Desenlace de un ítem durante la limpieza elevada.</summary>
public sealed class RemovalItemResult
{
    public required RemovalItemKind Kind { get; init; }

    public required string Identifier { get; init; }

    public bool Succeeded { get; init; }

    /// <summary>Motivo del fallo (controlado, apto para UI) o null.</summary>
    public string? Detail { get; init; }
}

/// <summary>Resultado de la limpieza elevada: ítem a ítem, sin drama.</summary>
public sealed class RemovalOutcome
{
    public required IReadOnlyList<RemovalItemResult> Items { get; init; }

    /// <summary>Al menos un servicio quedó "marked for deletion" → requiere reinicio.</summary>
    public bool RequiresRestart { get; init; }
}
