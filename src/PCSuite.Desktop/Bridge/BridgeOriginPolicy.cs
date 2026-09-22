namespace PCSuite.Desktop.Bridge;

/// <summary>
/// Origin validation for navigation and incoming bridge messages.
/// </summary>
public static class BridgeOriginPolicy
{
    /// <summary>
    /// True when <paramref name="uri"/> belongs exactly to <paramref name="origin"/>,
    /// preventing prefix attacks such as "https://app.pcsuite.local.evil.example".
    /// </summary>
    public static bool Matches(string uri, string origin) =>
        uri.StartsWith(origin, StringComparison.OrdinalIgnoreCase)
        && (uri.Length == origin.Length || uri[origin.Length] is '/' or '?' or '#');
}
