namespace PCSuite.Core.Updates;

/// <summary>Artefacto descargable del manifiesto (setup-win64, portable-win64…).</summary>
public sealed record UpdateArtifact
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Label { get; init; } = "";
    public string Url { get; init; } = "";
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";
}

/// <summary>Manifiesto de actualización (latest.json, formato de la propuesta §4).</summary>
public sealed class UpdateManifest
{
    public string Product { get; set; } = "";
    public string Channel { get; set; } = "stable";
    public string Version { get; set; } = "";
    public string ReleasedAt { get; set; } = "";
    public string ReleaseNotesUrl { get; set; } = "";
    public List<UpdateArtifact> Artifacts { get; set; } = [];
}

/// <summary>
/// Resultado del check. NUNCA lanza: un fallo de red es un resultado con
/// Available=false y Error con mensaje humano — la UI lo muestra sin dramatismo.
/// </summary>
public sealed record UpdateCheckResult
{
    public bool Available { get; init; }

    public string CurrentVersion { get; init; } = "";

    public string LatestVersion { get; init; } = "";

    /// <summary>Tamaño del instalador a descargar (bytes).</summary>
    public long SizeBytes { get; init; }

    public string? ReleaseNotesUrl { get; init; }

    /// <summary>Null cuando todo fue bien; mensaje humano cuando no.</summary>
    public string? Error { get; init; }
}

/// <summary>Interfaz del servicio de actualizaciones (Task 17.3/17.4).</summary>
public interface IPcsuiteUpdateService
{
    /// <summary>
    /// Consulta el manifiesto público (GET, timeout corto, no bloquea nada):
    /// ¿hay una versión mayor que la instalada? Devuelve el artefacto
    /// "setup-win64" con su SHA-256 para la descarga verificada.
    /// </summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Descarga el instalador a %TEMP% en streaming de 64 KB computando SHA-256
    /// en paralelo (mismo patrón que Local Share). Si el hash final NO coincide
    /// con el manifiesto: borra el archivo y lanza con mensaje claro — jamás
    /// se ofrecerá la instalación de algo no verificado. Devuelve la ruta del
    /// archivo verificado (recordada internamente para InstallAsync).
    /// </summary>
    Task<string> DownloadAsync(
        Action<long, long>? onProgress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lanza el instalador YA verificado (la ruta debe ser exactamente la que
    /// DownloadAsync devolvió en esta sesión — cualquier otra se rechaza).
    /// No espera su salida: el Setup pide UAC él solo si la instalación lo
    /// requiere, y la app se cierra para dejarle paso.
    /// </summary>
    Task InstallAsync(string verifiedPath, CancellationToken cancellationToken = default);
}
