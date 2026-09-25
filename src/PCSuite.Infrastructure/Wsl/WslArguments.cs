namespace PCSuite.Infrastructure.Wsl;

/// <summary>
/// argv de wsl fijos por operación (§12/§53): el frontend solo envía
/// payload cero y jamás argumentos de comando; aquí no hay parsing de
/// entrada, solo arreglos inmutables.
/// </summary>
internal static class WslArguments
{
    public static readonly string[] Version = ["--version"];

    public static readonly string[] Status = ["--status"];

    public static readonly string[] List = ["--list", "--verbose"];

    public static readonly string[] Update = ["--update"];

    public static readonly string[] Shutdown = ["--shutdown"];

    /// <summary>Arranca la distribución por defecto (no existe un verbo --start).</summary>
    public static readonly string[] Start = ["--exec", "true"];
}
