using PCSuite.Core.Processes;

namespace PCSuite.Application.Tests;

/// <summary>
/// Construye solicitudes que ejecutan el proceso mock
/// (tests/PCSuite.MockProcess) mediante <c>dotnet exec</c> con el
/// runtimeconfig del propio proyecto de tests. Así los tests no dependen de
/// ningún software instalado en la máquina.
/// </summary>
internal static class MockProcess
{
    private static readonly string MockDll = Path.Combine(AppContext.BaseDirectory, "PCSuite.MockProcess.dll");
    private static readonly string RuntimeConfig = Path.Combine(AppContext.BaseDirectory, "PCSuite.Application.Tests.runtimeconfig.json");

    public static ProcessRequest Request(params string[] arguments) => new()
    {
        FileName = "dotnet",
        Arguments = ["exec", "--runtimeconfig", RuntimeConfig, MockDll, .. arguments],
    };

    public static IReadOnlyList<string> SplitLines(string output)
        => output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
}
