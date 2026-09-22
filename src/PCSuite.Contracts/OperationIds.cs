namespace PCSuite.Contracts;

/// <summary>
/// Allow-listed operation identifiers. The frontend may only invoke
/// operations registered here; arbitrary command execution is never exposed.
/// </summary>
public static class OperationIds
{
    public const string GetSystemInfo = "system.info";
}
