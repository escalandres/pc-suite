namespace PCSuite.Desktop.Bridge;

/// <summary>
/// Locates the built React frontend (frontend/dist) relative to the running binary.
/// </summary>
public static class FrontendLocator
{
    public static string? FindDistDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "frontend", "dist");
            if (File.Exists(Path.Combine(candidate, "index.html")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
