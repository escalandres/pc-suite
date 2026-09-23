namespace PCSuite.Contracts;

/// <summary>
/// Payload de <see cref="OperationIds.WingetInstall"/>,
/// <see cref="OperationIds.WingetUpgrade"/> y
/// <see cref="OperationIds.WingetUninstall"/>. El frontend envía SOLO el id
/// del paquete: nunca una línea de comando completa (§10.1).
/// </summary>
public sealed class WinGetPackagePayload
{
    /// <summary>
    /// Id exacto del paquete (p. ej. "7zip.7zip"). Validado contra un patrón
    /// estricto en .NET antes de construir los argumentos de winget.
    /// </summary>
    public string PackageId { get; init; } = "";
}
