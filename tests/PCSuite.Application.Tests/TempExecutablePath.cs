namespace PCSuite.Application.Tests;

/// <summary>
/// Carpeta temporal con archivos falsos para pruebas de la PATH: los tests
/// crean sus propios "ejecutables" y no dependen del software instalado.
/// </summary>
internal sealed class TempExecutablePath : IDisposable
{
    public string DirectoryPath { get; } =
        Path.Combine(Path.GetTempPath(), "pcsuite-tests-" + Guid.NewGuid().ToString("N"));

    public TempExecutablePath()
    {
        Directory.CreateDirectory(DirectoryPath);
    }

    /// <summary>Crea un archivo vacío con el nombre indicado y devuelve su ruta.</summary>
    public string Add(string fileName)
    {
        var path = Path.Combine(DirectoryPath, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
            // El temporizador del SO puede retener archivos: no es un fallo del test.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
