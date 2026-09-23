using System.Text.RegularExpressions;
using PCSuite.Contracts;
using PCSuite.Core.WinGet;

namespace PCSuite.Infrastructure.WinGet;

/// <summary>
/// Único punto donde se valida la entrada del frontend y se construyen los
/// argv de winget. Cada operación usa un arreglo FIJO de argumentos pasado al
/// proceso como lista (sin shell): una entrada inválida lanza
/// <see cref="WinGetException"/> ANTES de crear cualquier proceso. La
/// validación se repite dentro del proceso elevado (defensa en profundidad).
/// </summary>
internal static class WinGetArguments
{
    private const int MaxIdLength = 128;
    private const int MaxQueryLength = 128;

    /// <summary>
    /// Id de paquete estricto: primer carácter alfanumérico (nunca '-'), luego
    /// solo letras, dígitos y . _ + -. Sin espacios, sin controles, ≤128.
    /// </summary>
    private static readonly Regex PackageIdPattern =
        new("^[A-Za-z0-9][A-Za-z0-9._+-]*$", RegexOptions.Compiled);

    /// <summary>Valida y devuelve el id; inválido → WinGetException(MALFORMED_MESSAGE).</summary>
    public static string ValidatePackageId(string? packageId)
    {
        if (string.IsNullOrEmpty(packageId)
            || packageId.Length > MaxIdLength
            || !PackageIdPattern.IsMatch(packageId))
        {
            throw new WinGetException(
                BridgeErrorCodes.MalformedMessage,
                "El id del paquete no es válido: se admiten solo letras, dígitos y . _ + - (máximo 128 caracteres).");
        }

        return packageId;
    }

    /// <summary>
    /// Valida y normaliza (trim) la búsqueda: 1..128 caracteres, sin
    /// caracteres de control y sin guion inicial (nunca interpretado como flag).
    /// </summary>
    public static string ValidateQuery(string? query)
    {
        var trimmed = query?.Trim() ?? string.Empty;

        if (trimmed.Length == 0
            || trimmed.Length > MaxQueryLength
            || trimmed[0] == '-'
            || trimmed.Any(char.IsControl))
        {
            throw new WinGetException(
                BridgeErrorCodes.MalformedMessage,
                "La búsqueda no es válida: usa de 1 a 128 caracteres, sin controles y sin guion inicial.");
        }

        return trimmed;
    }

    /// <summary>Argumentos de <c>winget --version</c> (detección).</summary>
    public static string[] Version() => ["--version"];

    /// <summary>Búsqueda libre limitada a 25 filas del origen winget.</summary>
    public static string[] Search(string query) =>
    [
        "search",
        ValidateQuery(query),
        "--source", "winget",
        "--count", "25",
        "--disable-interactivity",
        "--accept-source-agreements",
    ];

    /// <summary>Paquetes instalados de origen winget.</summary>
    public static string[] List() =>
    [
        "list",
        "--source", "winget",
        "--disable-interactivity",
        "--accept-source-agreements",
    ];

    /// <summary>
    /// Lista de actualizables (SIN --id ni --all: winget imprime las tablas).
    /// </summary>
    public static string[] Updates() =>
    [
        "upgrade",
        "--source", "winget",
        "--disable-interactivity",
        "--accept-source-agreements",
    ];

    public static string[] Install(string packageId) =>
    [
        "install",
        "--id", ValidatePackageId(packageId),
        "--exact",
        "--source", "winget",
        "--silent",
        "--accept-package-agreements",
        "--accept-source-agreements",
        "--disable-interactivity",
    ];

    public static string[] Upgrade(string packageId) =>
    [
        "upgrade",
        "--id", ValidatePackageId(packageId),
        "--exact",
        "--source", "winget",
        "--silent",
        "--accept-package-agreements",
        "--accept-source-agreements",
        "--disable-interactivity",
    ];

    public static string[] UpgradeAll() =>
    [
        "upgrade",
        "--all",
        "--source", "winget",
        "--silent",
        "--accept-package-agreements",
        "--accept-source-agreements",
        "--disable-interactivity",
    ];

    public static string[] Uninstall(string packageId) =>
    [
        "uninstall",
        "--id", ValidatePackageId(packageId),
        "--exact",
        "--source", "winget",
        "--silent",
        "--disable-interactivity",
        "--accept-source-agreements",
        // Sin --accept-package-agreements: el flag no existe en uninstall.
    ];
}
