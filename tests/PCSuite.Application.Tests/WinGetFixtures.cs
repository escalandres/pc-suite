namespace PCSuite.Application.Tests;

/// <summary>
/// Salidas REALES de winget (locale es-ES, v1.29.380) capturadas por la ruta
/// de producción (argv fijos + ProcessRunner + UTF-8) y congeladas en
/// <c>Fixtures/</c>: los tests no dependen de winget ni del software
/// instalado en la máquina.
/// </summary>
internal static class WinGetFixtures
{
    public static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
