using System.Reflection;

namespace PCSuite.Infrastructure;

public static class AppInfo
{
    public static string Version { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";
}
