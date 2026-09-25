namespace PCSuite.Application.Tests;

/// <summary>
/// Salidas REALES de wsl.exe (locale es-ES, WSL 2.7.14.0) capturadas por la
/// ruta de producción (argv fijos + ProcessRunner + UTF-16LE) y congeladas en
/// <c>Fixtures/</c>: los tests no dependen de wsl ni de las distribuciones
/// instaladas en la máquina. Los fixtures viven en disco en UTF-8 y
/// File.ReadAllText los decodifica así por omisión.
/// </summary>
internal static class WslFixtures
{
    public static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
