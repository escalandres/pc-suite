namespace PCSuite.Infrastructure;

public static class PCSuitePaths
{
    public static string RootDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCSuite");

    public static string LogDirectory => Path.Combine(RootDirectory, "Logs");

    public static string DataDirectory => Path.Combine(RootDirectory, "Data");
}
