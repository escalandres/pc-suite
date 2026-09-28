namespace PCSuite.Contracts;

/// <summary>
/// Allow-listed operation identifiers. The frontend may only invoke
/// operations registered here; arbitrary command execution is never exposed.
/// </summary>
public static class OperationIds
{
    public const string GetSystemInfo = "system.info";

    /// <summary>Devuelve los metadatos de todas las tools registradas.</summary>
    public const string ToolsList = "tools.list";

    /// <summary>Busca tools por texto libre sobre sus metadatos.</summary>
    public const string ToolsSearch = "tools.search";

    /// <summary>Ejecuta una tool por su id con un payload tipado.</summary>
    public const string ToolExecute = "tool.execute";

    /// <summary>Estado (instalación, versión, ruta) de las tools de desarrollador.</summary>
    public const string DeveloperList = "developer.list";

    /// <summary>Comprueba actualizaciones de una tool de desarrollador.</summary>
    public const string DeveloperCheckUpdate = "developer.checkUpdate";

    /// <summary>Ejecuta el self-update nativo de una tool de desarrollador.</summary>
    public const string DeveloperUpdate = "developer.update";

    /// <summary>Lanza la interfaz interactiva (REPL/TUI) de una tool.</summary>
    public const string DeveloperLaunch = "developer.launch";

    // --- WinGet (Task 006 / Stage 7) -------------------------------------
    // El frontend envía SIEMPRE payloads estructurados (p. ej. {"packageId"}):
    // jamás una línea "winget …" arbitraria; .NET valida la entrada y construye
    // los argumentos permitidos.

    /// <summary>Detección: winget instalado/no, versión, ruta y si corre elevado.</summary>
    public const string WingetDetect = "winget.detect";

    /// <summary>Busca paquetes en el índice winget (payload: {"query"}).</summary>
    public const string WingetSearch = "winget.search";

    /// <summary>Lista de paquetes instalados desde el origen winget.</summary>
    public const string WingetList = "winget.list";

    /// <summary>Paquetes con actualización disponible.</summary>
    public const string WingetUpdates = "winget.updates";

    /// <summary>Instala un paquete (payload: {"packageId"}); puede pedir UAC.</summary>
    public const string WingetInstall = "winget.install";

    /// <summary>Actualiza un paquete concreto (payload: {"packageId"}); puede pedir UAC.</summary>
    public const string WingetUpgrade = "winget.upgrade";

    /// <summary>Actualiza todo lo actualizable; puede pedir UAC.</summary>
    public const string WingetUpgradeAll = "winget.upgradeAll";

    /// <summary>Desinstala un paquete (payload: {"packageId"}); puede pedir UAC.</summary>
    public const string WingetUninstall = "winget.uninstall";

    // Operaciones WSL (Task 007): TODAS de payload cero — el frontend no
    // envía argumentos (§12/§53) y los argv son fijos en WslArguments.
    /// <summary>Estado de WSL: instalación, versión, distribución por defecto y running/stopped.</summary>
    public const string WslStatus = "wsl.status";

    /// <summary>Distribuciones WSL con su estado y versión de plataforma.</summary>
    public const string WslList = "wsl.list";

    /// <summary>Actualiza el componente WSL (wsl --update); puede pedir UAC.</summary>
    public const string WslUpdate = "wsl.update";

    /// <summary>Apaga el subsistema WSL (wsl --shutdown); nunca eleva.</summary>
    public const string WslShutdown = "wsl.shutdown";

    /// <summary>Arranca la distribución por defecto (wsl --exec true); nunca eleva.</summary>
    public const string WslStart = "wsl.start";

    // Operaciones ClamAV (Task 008 / §13): la ruta del customScan viaja como
    // payload estructurado validado en el servicio; el resto es de payload cero
    // y los argv (clamscan/freshclam) son arreglos fijos, nunca una línea
    // "clamscan …" arbitraria.

    /// <summary>Detección: clamscan instalado/no, ruta, versión y definiciones.</summary>
    public const string ClamavStatus = "clamav.status";

    /// <summary>Instala ClamAV (paquete fijo Cisco.ClamAV vía winget); puede pedir UAC.</summary>
    public const string ClamavInstall = "clamav.install";

    /// <summary>Actualiza las definiciones con freshclam; puede pedir UAC por permisos.</summary>
    public const string ClamavUpdateDefinitions = "clamav.updateDefinitions";

    /// <summary>Escaneo rápido de ubicaciones comunes del usuario.</summary>
    public const string ClamavQuickScan = "clamav.quickScan";

    /// <summary>Escaneo completo de los discos fijos.</summary>
    public const string ClamavFullScan = "clamav.fullScan";

    /// <summary>Escaneo de una ruta absoluta existente (payload: {"path"}).</summary>
    public const string ClamavCustomScan = "clamav.customScan";

    /// <summary>Historial de escaneos persistido (más reciente primero).</summary>
    public const string ClamavHistory = "clamav.history";

    // Operaciones SSH (Task 009 / §5.1–5.2): payloads estructurados validados
    // en el servicio (ruta, algoritmo, tamaño, nombre de archivo); la
    // generación invoca ssh-keygen con argv fijo y el resto (listado,
    // inspección, fingerprint e importación) es parseo .NET puro.

    /// <summary>Detección: ssh-keygen instalado/no y ruta resuelta.</summary>
    public const string SshDetect = "ssh.detect";

    /// <summary>Claves públicas existentes (por defecto en ~/.ssh).</summary>
    public const string SshList = "ssh.list";

    /// <summary>Generación de par (payload: algorithm/bits/comment/directory/passphrase).</summary>
    public const string SshGenerate = "ssh.generate";

    /// <summary>Inspección de un archivo .pub o contenedor openssh-key-v1 (payload: {"path"}).</summary>
    public const string SshInspect = "ssh.inspect";

    /// <summary>Importación de una clave pública (payload: key/fileName/directory).</summary>
    public const string SshImportPublicKey = "ssh.importPublicKey";

    /// <summary>Crea ~/.ssh si falta y lo abre en el Explorador.</summary>
    public const string SshOpenDirectory = "ssh.openDirectory";

    // Operaciones PGP (Task 009 / §6): BouncyCastle 100% en proceso (nunca
    // gpg, que no existe en Windows por defecto); los payloads son texto y
    // claves armoured acotados, y el material privado / los passphrases
    // jamás viajan en claro ni en argv de procesos.

    /// <summary>Generación de par (payload: name/email/algorithm/bits/expirationDays/passphrase).</summary>
    public const string PgpGenerateKey = "pgp.generateKey";

    /// <summary>Claves del keyring propio (%APPDATA%\PCSuite\pgp); payload cero.</summary>
    public const string PgpListKeys = "pgp.listKeys";

    /// <summary>Importación de una clave armoured (pública o privada): payload {"armoredKey"}.</summary>
    public const string PgpImportKey = "pgp.importKey";

    /// <summary>Exportación armoured por fingerprint (payload: fingerprint/includePrivate).</summary>
    public const string PgpExportKey = "pgp.exportKey";

    /// <summary>Cifrado de texto para destinatarios (payload: text/recipients).</summary>
    public const string PgpEncrypt = "pgp.encrypt";

    /// <summary>Descifrado de un mensaje armoured (payload: armoredMessage/passphrase).</summary>
    public const string PgpDecrypt = "pgp.decrypt";

    /// <summary>Firma separada de texto con clave privada (payload: text/fingerprint/passphrase).</summary>
    public const string PgpSign = "pgp.sign";

    /// <summary>Verificación de firma armoured (payload: text/signature).</summary>
    public const string PgpVerify = "pgp.verify";

    // Operaciones Limpiador (Task 011 / §16 "Temporary file cleanup"): el
    // frontend envía SOLO ids de categoría de la lista fija validada en el
    // servicio — jamás rutas — y .NET borra únicamente dentro de las raíces
    // fijas (Temp de usuario, C:\Windows\Temp, SoftwareDistribution\Download).

    /// <summary>Tamaños y ficheros por categoría de temporales (payload cero).</summary>
    public const string CleanerScan = "cleaner.scan";

    /// <summary>Borra las categorías pedidas (payload: {"categories":[ids]}).</summary>
    public const string CleanerClean = "cleaner.clean";

    // Operaciones Seguridad (Task 012 / §14 "Security Center"): SOLO LECTURA —
    // el frontend no envía nada y el backend jamás modifica ajustes de
    // seguridad sensibles (§14 exige confirmación explícita del usuario; las
    // acciones llegarán en una task futura con confirmación + registro).

    /// <summary>Snapshot de solo lectura: Defender, firewall, puertos, antivirus y diagnóstico (payload cero).</summary>
    public const string SecurityGetStatus = "security.getStatus";
}
