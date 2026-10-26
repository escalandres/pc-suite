namespace PCSuite.Contracts;

/// <summary>
/// Allow-listed operation identifiers. The frontend may only invoke
/// operations registered here; arbitrary command execution is never exposed.
/// </summary>
public static class OperationIds
{
    public const string GetSystemInfo = "system.info";

    /// <summary>
    /// Selector de archivos NATIVO de Windows (multiselección). Lo usan las
    /// herramientas que necesitan rutas absolutas — el <input type=file>
    /// del WebView no las expone: solo da nombres sueltos.
    /// </summary>
    public const string SystemPickFiles = "system.pickFiles";

    /// <summary>
    /// Copia texto al portapapeles de Windows (payload: {"text"}). El
    /// navigator.clipboard de Chromium en WebView2 exige el permiso
    /// clipboard-write, que el host no concede — fallaba EN SILENCIO
    /// (el botón "Copiar" no hacía nada). El host escribe el portapapeles
    /// nativo en su lugar.
    /// </summary>
    public const string SystemWriteClipboard = "system.writeClipboard";

    /// <summary>
    /// Diálogo NATIVO de Windows para elegir UNA carpeta (devuelve su ruta
    /// absoluta). El WebView no puede proveer rutas de carpetas — igual que
    /// system.pickFiles (Task 026: escaneo personalizado de ClamAV).
    /// </summary>
    public const string SystemPickFolder = "system.pickFolder";

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

    /// <summary>Carpetas que escaneará el escaneo rápido (solo lectura; el modal de la UI las muestra antes del UAC).</summary>
    public const string ClamavQuickTargets = "clamav.quickTargets";

    /// <summary>Escaneo completo de los discos fijos.</summary>
    public const string ClamavFullScan = "clamav.fullScan";

    /// <summary>Escaneo de una ruta absoluta existente (payload: {"path"}).</summary>
    public const string ClamavCustomScan = "clamav.customScan";

    /// <summary>Historial de escaneos persistido (más reciente primero).</summary>
    public const string ClamavHistory = "clamav.history";

    /// <summary>
    /// El escaneo en curso (Task 025.1): re-adjunta la UI tras navegar o
    /// recargar durante un escaneo largo — devuelve {jobId, kind, startedAt}
    /// o vacío si no hay ninguno activo. Payload cero, solo lectura.
    /// </summary>
    public const string ClamavActiveScan = "clamav.activeScan";

    // Operaciones de jobs (larga duración): los escaneos clamav.* ya no
    // retienen la respuesta — devuelven {jobId} y la UI consulta/cancela.

    /// <summary>Snapshot de un job por id (payload: {"jobId"}).</summary>
    public const string JobGet = "job.get";

    /// <summary>Cancela un job en curso (payload: {"jobId"}).</summary>
    public const string JobCancel = "job.cancel";

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

    // Operaciones Plugins (Task 013 / §17 "Plugins extensibles"): sistema de
    // complementos extensibles para PCSuite. Todas las operaciones son de solo
    // lectura o con payloads validados; sin ejecución arbitraria de comandos.

    /// <summary>Lista de plugins instalados (payload cero).</summary>
    public const string PluginList = "plugin.list";

    /// <summary>Comprobar actualización de un plugin (payload: { pluginId }).</summary>
    public const string PluginCheckUpdate = "plugin.checkUpdate";

    /// <summary>Lanzar un plugin (payload: { pluginId }).</summary>
    public const string PluginLaunch = "plugin.launch";

    /// <summary>Estado de un plugin (payload cero).</summary>
    public const string PluginStatus = "plugin.status";

    /// <summary>Abrir una ruta en el Explorador de Windows (payload: { path }).</summary>
    public const string ExplorerOpenPath = "explorer.openPath";

    /// <summary>Abrir una URL http/https en el navegador del sistema (payload: { url }).</summary>
    public const string ExplorerOpenUrl = "explorer.openUrl";

    /// <summary>Alterna el piloto Blazor WebView (Task 015; solo en Debug).</summary>
    public const string BlazorToggle = "blazor.toggle";

    // Operaciones Local Share (Task 016): transferencia de archivos en LAN.
    // El frontend envía payloads estructurados validados en el servicio;
    // el protocolo TCP/TLS es construcción interna del host .NET.

    /// <summary>Estado del servicio (stopped/running/error + puerto + config resumida).</summary>
    public const string LocalShareStatus = "localshare.status";

    /// <summary>Inicia el listener TCP+TLS y el responder mDNS (payload cero).</summary>
    public const string LocalShareStart = "localshare.start";

    /// <summary>Detiene el listener y cancela transferencias activas (payload cero).</summary>
    public const string LocalShareStop = "localshare.stop";

    /// <summary>Lista dispositivos conocidos: detectados por mDNS + emparejados (payload cero).</summary>
    public const string LocalShareDevices = "localshare.devices";

    /// <summary>Empareja un dispositivo por IP:puerto (payload: { address, port }).</summary>
    public const string LocalSharePair = "localshare.pair";

    /// <summary>Acepta una solicitud de emparejamiento entrante (payload: { requestId }).</summary>
    public const string LocalSharePairAccept = "localshare.pairAccept";

    /// <summary>Rechaza una solicitud de emparejamiento entrante (payload: { requestId }).</summary>
    public const string LocalSharePairReject = "localshare.pairReject";

    /// <summary>Cancela un emparejamiento iniciado por este equipo (payload: { requestId }).</summary>
    public const string LocalSharePairCancel = "localshare.pairCancel";

    /// <summary>Revoca la confianza de un dispositivo (payload: { deviceId }).</summary>
    public const string LocalShareRevoke = "localshare.revoke";

    /// <summary>Inicia un envío de archivos (payload: { deviceId, paths }).</summary>
    public const string LocalShareSend = "localshare.send";

    /// <summary>Mensaje de texto a un par emparejado (Task 016.8): mismo pipeline que los archivos.</summary>
    public const string LocalShareSendMessage = "localshare.sendMessage";

    /// <summary>Acepta una solicitud entrante (payload: { transferId }).</summary>
    public const string LocalShareAccept = "localshare.accept";

    /// <summary>Rechaza una solicitud entrante (payload: { transferId }).</summary>
    public const string LocalShareReject = "localshare.reject";

    /// <summary>Lista transferencias activas + historial (payload cero).</summary>
    public const string LocalShareTransfers = "localshare.transfers";

    /// <summary>Cancela una transferencia en curso (payload: { transferId }).</summary>
    public const string LocalShareCancelTransfer = "localshare.cancelTransfer";

    /// <summary>Lee la configuración (payload cero) o la actualiza (payload con campos opcionales).</summary>
    public const string LocalShareConfig = "localshare.config";

    /// <summary>Limpia el historial de transferencias terminadas (payload cero).</summary>
    public const string LocalShareClearHistory = "localshare.clearHistory";

    /// <summary>Diagnóstico de firewall de Local Share (solo lectura, sin elevación).</summary>
    public const string LocalShareFirewallStatus = "localshare.firewallStatus";

    /// <summary>Crea la regla de firewall entrante (UAC puntual vía ElevationManager).</summary>
    public const string LocalShareFirewallRule = "localshare.firewallRule";

    /// <summary>Pausa un envío en curso (payload: { transferId }).</summary>
    public const string LocalSharePauseTransfer = "localshare.pauseTransfer";

    /// <summary>Reanuda un envío pausado (payload: { transferId }).</summary>
    public const string LocalShareResumeTransfer = "localshare.resumeTransfer";

    // ---------------- Task 017: actualizaciones ----------------

    /// <summary>
    /// Consulta el manifiesto público (latest.json): ¿hay versión mayor que la
    /// instalada? Timeout corto; un fallo de red es Available=false (la app
    /// funciona igual). Payload cero.
    /// </summary>
    public const string UpdateCheck = "update.check";

    /// <summary>
    /// Descarga el instalador como Job (patrón existente: jobId + job.event
    /// con progreso y desenlace). SHA-256 en streaming: discrepancia → job
    /// Failed y el archivo se borra.
    /// </summary>
    public const string UpdateDownload = "update.download";

    /// <summary>
    /// Lanza el instalador YA verificado (payload: { path } — la que devolvió
    /// el job de descarga). Única operación que ejecuta algo: requiere el
    /// botón explícito del usuario. Tras lanzar, la app se cierra sola.
    /// </summary>
    public const string UpdateInstall = "update.install";

    // ---------------- Task 033: Archivos ----------------

    /// <summary>Encripta un archivo con AES-256-CBC + PBKDF2 (Task 033).</summary>
    public const string FilesEncrypt = "files.encrypt";

    /// <summary>Desencripta un archivo .pcsuite-enc (Task 033).</summary>
    public const string FilesDecrypt = "files.decrypt";

    /// <summary>Oculta texto o un archivo dentro de una imagen PNG (LSB, Task 033).</summary>
    public const string FilesStegoEmbed = "files.stegoEmbed";

    /// <summary>Extrae el payload oculto de una imagen PNG (Task 033).</summary>
    public const string FilesStegoExtract = "files.stegoExtract";

    /// <summary>Sobrescribe y borra un archivo de forma segura (Task 033).</summary>
    public const string FilesShred = "files.shred";

    /// <summary>Hash multi-algoritmo de un archivo (Task 033).</summary>
    public const string FilesHash = "files.hash";

    // ---------------- Task 034: Cifrador ----------------

    /// <summary>Codifica/decodifica texto con el algoritmo elegido (Task 034).</summary>
    public const string CipherEncode = "cipher.encode";

    /// <summary>Hash multi-algoritmo de texto (Task 034).</summary>
    public const string CipherHash = "cipher.hash";

    /// <summary>Cifra texto con AES-256-GCM (Task 034).</summary>
    public const string CipherEncryptAes = "cipher.encryptAes";

    /// <summary>Descifra texto AES-256-GCM (Task 034).</summary>
    public const string CipherDecryptAes = "cipher.decryptAes";

    /// <summary>Cifra/decifra texto con XOR (Task 034).</summary>
    public const string CipherXor = "cipher.xor";

    // ---------------- Task 019: certificados ----------------

    /// <summary>Genera un certificado X.509 autofirmado (Task 19.1).</summary>
    public const string CertX509Generate = "cert.x509.generate";

    /// <summary>Inspecciona un archivo de certificado/clave: reconoce el formato (Task 19.3).</summary>
    public const string CertInspect = "cert.inspect";

    /// <summary>Convierte entre formatos: PEM↔DER (Task 19.4).</summary>
    public const string CertConvert = "cert.convert";

    /// <summary>Firma un archivo con una clave PGP privada del keyring (Task 19.2 / 17.6).</summary>
    public const string CertPgpSignFile = "cert.pgp.signFile";

    /// <summary>Genera un CSR PKCS#10 + clave privada (Task 19.5).</summary>
    public const string CertCsrGenerate = "cert.csr.generate";

    /// <summary>Verifica una firma PGP detached (.sig) contra el archivo original (Task 19.2).</summary>
    public const string CertPgpVerifyFile = "cert.pgp.verifyFile";

    // ---------------- Task 020: desinstalador forzado ----------------

    /// <summary>Lista las apps del registro de desinstalación de Windows (incluye las que winget no ve).</summary>
    public const string UninstallList = "uninstall.list";

    /// <summary>Construye el plan de eliminación de una app (solo lectura, sin elevación).</summary>
    public const string UninstallDiscover = "uninstall.discover";

    /// <summary>Lanza el desinstalador oficial de la app (elevado, UAC puntual).</summary>
    public const string UninstallRunOfficial = "uninstall.runOfficial";

    /// <summary>Aplica la limpieza de residuos sobre el plan aprobado (elevado, UAC puntual).</summary>
    public const string UninstallCleanup = "uninstall.cleanup";
}
