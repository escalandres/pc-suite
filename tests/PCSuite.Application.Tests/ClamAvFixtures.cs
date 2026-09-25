namespace PCSuite.Application.Tests;

/// <summary>
/// Salidas sintéticas de ClamAV (Task 008). ClamAV NO está instalado en la
/// máquina de desarrollo, así que estas cadenas se construyen a mano con el
/// formato documentado de clamscan/freshclam (las etiquetas del resumen son
/// SIEMPRE inglesas): los tests no dependen de software externo.
/// </summary>
internal static class ClamAvFixtures
{
    /// <summary>clamscan --version: motor y build de definiciones.</summary>
    public const string Version = "ClamAV 1.5.4/7123456 Sat Sep 20 08:29:48 2025";

    /// <summary>Escaneo limpio: resumen completo y 0 amenazas.</summary>
    public const string CleanScan = """
        ----------- SCAN SUMMARY -----------
        Known viruses: 7123456
        Engine version: 1.5.4
        Scanned directories: 42
        Scanned files: 1234
        Infected files: 0
        """;

    /// <summary>
    /// Dos amenazas (una con ": " dentro de la propia ruta) + resumen con el
    /// recuento exacto.
    /// </summary>
    public const string InfectedScan = """
        C:\Users\test\Downloads\evil.com: Win.Trojan.FakeMS-123 FOUND
        C:\carpeta con: dos puntos\eicar.com: Eicar-Test-Signature FOUND
        ----------- SCAN SUMMARY -----------
        Known viruses: 7123456
        Engine version: 1.5.4
        Scanned directories: 5
        Scanned files: 99
        Infected files: 2
        """;

    /// <summary>Exit 2 con resumen: escaneo parcial interpretable.</summary>
    public const string PartialScan = """
        C:\denied\archivo.dat: Permiso denegado
        ----------- SCAN SUMMARY -----------
        Known viruses: 7123456
        Engine version: 1.5.4
        Scanned directories: 3
        Scanned files: 10
        Infected files: 0
        """;

    /// <summary>Fallo sin resumen: el escaneo no es interpretable.</summary>
    public const string ErrorNoSummary = "ERROR: Can't parse options";

    /// <summary>freshclam sin privilegios (strerror(EACCES) en inglés).</summary>
    public const string PermissionDenied =
        @"ERROR: Can't create/modify file 'C:\Program Files\ClamAV\main.cvd': Permission denied";

    /// <summary>freshclam completado sin errores.</summary>
    public const string FreshclamOk = "ClamAV update process completed.";
}
