namespace PCSuite.Infrastructure;

public static class PCSuitePaths
{
    public static string RootDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCSuite");

    public static string LogDirectory => Path.Combine(RootDirectory, "Logs");

    public static string DataDirectory => Path.Combine(RootDirectory, "Data");

    /// <summary>
    /// Keyring PGP (Task 009 / §6): %APPDATA%\PCSuite\pgp — Roaming para que
    /// las claves acompañen al perfil de usuario.
    /// </summary>
    public static string PgpKeyringDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PCSuite",
            "pgp");
}
