namespace PCSuite.Contracts;

public static class EventIds
{
    public const string AppReady = "app.ready";

    /// <summary>Progreso de actualización de definiciones ClamAV (freshclam).</summary>
    public const string ClamAvFreshclamProgress = "clamav.freshclam.progress";

    /// <summary>Progreso de escaneo ClamAV (clamscan).</summary>
    public const string ClamAvScanProgress = "clamav.scan.progress";

    /// <summary>Ciclo de vida de jobs: estado, progreso y resultado.</summary>
    public const string JobEvent = "job.event";
}
