using System.Text;
using Microsoft.Extensions.Logging;
using PCSuite.Contracts;
using PCSuite.Core.AI;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure.Developers;

namespace PCSuite.Infrastructure.AI;

/// <summary>
/// Implementación de <see cref="IAiService"/> sobre el CLI de Python + markItDown
/// (Task 038 / §IA). PCSuite orquesta, no es el motor de conversión:
/// - La entrada del frontend solo llega como payload tipado; la validación de
///   la ruta y los argv fijos viven aquí y en AiArguments (sin shell, nunca una
///   línea "python -m markitdown …" arbitraria).
/// - La detección ejecuta `python --version` y `python -m pip show markitdown`.
/// - La instalación ejecuta `python -m pip install markitdown` (puede pedir UAC
///   por operación si el entorno de Python lo requiere).
/// - La conversión ejecuta `python -m markitdown <ruta>` y captura stdout como
///   Markdown generado. Puede tardar minutos (OCR, transcripción, PDFs grandes)
///   → sin tope de reloj fijo (lección 024); el control es del usuario.
/// - Los fallos controlados vuelven como AiException (código + mensaje aptos
///   para la UI), jamás stack traces.
/// </summary>
public sealed class AiService : IAiService
{
    private const string NotInstalledMessage =
        "markItDown no está instalado. Usa ai.install para instalarlo (requiere Python).";

    private const string ReadTimeoutMessage =
        "Python no respondió a tiempo. Inténtalo de nuevo.";

    private const string InstallTimeoutMessage =
        "La instalación de markItDown superó el tiempo límite.";

    private const string ConvertTimeoutMessage =
        "La conversión a Markdown superó el tiempo límite. Intenta con un archivo más pequeño.";

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);
    // Sin tope de reloj para la conversión (lección 024): los archivos grandes
    // pueden tardar minutos. El control es del usuario (cancelación vía puente).

    private readonly ExecutableLocator _locator;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<AiService>? _logger;

    public AiService(
        ExecutableLocator locator,
        IProcessRunner processRunner,
        ILogger<AiService>? logger = null)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _logger = logger;
    }

    public async Task<AiStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var python = FindPython();
        if (python is null)
        {
            return new AiStatus(false, null, null, null, "Python no está instalado en el sistema.");
        }

        var version = await RunAsync(
            python,
            AiArguments.PythonVersion,
            ReadTimeout,
            ReadTimeoutMessage,
            cancellationToken).ConfigureAwait(false);

        var pythonVersion = version is { ExitCode: 0 }
            ? AiParser.ParsePythonVersion(version.StandardOutput)
            : null;

        var markItDown = await RunAsync(
            python,
            AiArguments.PipShowMarkItDown,
            ReadTimeout,
            ReadTimeoutMessage,
            cancellationToken).ConfigureAwait(false);

        var markItDownVersion = markItDown is { ExitCode: 0 }
            ? AiParser.ParseMarkItDownVersion(markItDown.StandardOutput)
            : null;

        var installed = markItDown is { ExitCode: 0 };
        var detail = installed
            ? null
            : "markItDown no está instalado. Ejecuta ai.install para instalarlo.";

        return new AiStatus(installed, python, pythonVersion, markItDownVersion, detail);
    }

    public async Task<AiMutationOutcome> InstallAsync(CancellationToken cancellationToken = default)
    {
        var python = FindPython();
        if (python is null)
        {
            return new AiMutationOutcome(false, -1, false,
                "Python no está instalado. Instala Python primero (https://www.python.org).");
        }

        try
        {
            var result = await _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = python,
                    Arguments = AiArguments.PipInstallMarkItDown,
                    Timeout = InstallTimeout,
                    RequiresElevation = false,
                },
                cancellationToken).ConfigureAwait(false);

            return new AiMutationOutcome(
                result.ExitCode == 0,
                result.ExitCode,
                false,
                result.ExitCode == 0
                    ? null
                    : (AiParser.PipDetail(result.StandardOutput) ?? $"pip terminó con código {result.ExitCode}."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProcessTimeoutException)
        {
            throw new AiException(BridgeErrorCodes.AiFailed, InstallTimeoutMessage);
        }
        catch (ElevationDeclinedException)
        {
            throw new AiException(
                BridgeErrorCodes.AiFailed,
                "El usuario canceló el control de cuentas de usuario.");
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "No se pudo instalar markItDown.");
            throw new AiException(BridgeErrorCodes.AiFailed, "No se pudo instalar markItDown.");
        }
    }

    public string ValidateConvertPath(string? path)
    {
        const string errorCode = BridgeErrorCodes.AiFailed;

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new AiException(errorCode, "Indica una ruta absoluta para convertir.");
        }

        if (path.Contains('\0'))
        {
            throw new AiException(errorCode, "La ruta no es válida.");
        }

        if (!Path.IsPathRooted(path))
        {
            throw new AiException(errorCode, "La ruta debe ser absoluta (p. ej. C:\\Descargas\\documento.pdf).");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new AiException(errorCode, "La ruta no es válida.");
        }

        if (!File.Exists(fullPath))
        {
            throw new AiException(errorCode, "El archivo indicado no existe.");
        }

        return fullPath;
    }

    public async Task<AiConvertOutcome> ConvertAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var python = FindPython();
        if (python is null)
        {
            throw new AiException(BridgeErrorCodes.AiNotInstalled, "Python no está instalado.");
        }

        // Verificar que markItDown esté disponible antes de intentar convertir.
        var markItDownCheck = await RunAsync(
            python,
            AiArguments.PipShowMarkItDown,
            ReadTimeout,
            ReadTimeoutMessage,
            cancellationToken).ConfigureAwait(false);

        if (markItDownCheck is { ExitCode: not 0 })
        {
            throw new AiException(
                BridgeErrorCodes.AiNotInstalled,
                "markItDown no está instalado. Usa ai.install para instalarlo primero.");
        }

        try
        {
            var result = await _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = python,
                    Arguments = AiArguments.MarkItDownConvert(path),
                    // Timeout = null: sin tope de reloj (lección 024).
                    Timeout = null,
                    RequiresElevation = false,
                    WorkingDirectory = Environment.SystemDirectory,
                },
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                return new AiConvertOutcome(
                    false,
                    result.ExitCode,
                    null,
                    AiParser.ConvertDetail(result.StandardError) ?? $"markItDown terminó con código {result.ExitCode}.");
            }

            // Generar el archivo .md en la misma ubicación que el original.
            var outputPath = DeriveOutputPath(path);
            try
            {
                await File.WriteAllTextAsync(outputPath, result.StandardOutput ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "No se pudo escribir el archivo Markdown de salida {OutputPath}.", outputPath);
                return new AiConvertOutcome(
                    false,
                    -1,
                    null,
                    $"markItDown generó el contenido pero no se pudo escribir el archivo de salida: {exception.Message}");
            }

            return new AiConvertOutcome(true, 0, outputPath, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProcessTimeoutException)
        {
            throw new AiException(BridgeErrorCodes.AiFailed, ConvertTimeoutMessage);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "No se pudo convertir el archivo a Markdown.");
            throw new AiException(BridgeErrorCodes.AiConversionFailed, "No se pudo convertir el archivo a Markdown.");
        }
    }

    /// <summary>
    /// Deriva la ruta del archivo .md de salida junto al archivo original.
    /// Si ya existe, añade sufijo (1), (2), etc. para no sobrescribir.
    /// </summary>
    private static string DeriveOutputPath(string inputPath)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? string.Empty;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
        var candidate = Path.Combine(directory, $"{fileNameWithoutExt}.md");

        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var i = 1; i < 100; i++)
        {
            var numbered = Path.Combine(directory, $"{fileNameWithoutExt} ({i}).md");
            if (!File.Exists(numbered))
            {
                return numbered;
            }
        }

        return candidate;
    }

    private string? FindPython() =>
        _locator.Find("python") ?? _locator.Find("python3");

    private string? FindOllama() =>
        _locator.Find("ollama");

    public async Task<OllamaStatus> GetOllamaStatusAsync(CancellationToken cancellationToken = default)
    {
        var ollama = FindOllama();
        if (ollama is null)
        {
            return new OllamaStatus(false, null, null, "Ollama no está instalado en el sistema.");
        }

        var version = await RunAsync(
            ollama,
            AiArguments.OllamaVersion,
            ReadTimeout,
            ReadTimeoutMessage,
            cancellationToken).ConfigureAwait(false);

        var ollamaVersion = version is { ExitCode: 0 }
            ? AiParser.ParseOllamaVersion(version.StandardOutput)
            : null;

        var installed = version is { ExitCode: 0 };
        var detail = installed
            ? null
            : "Ollama no respondió correctamente. Ejecuta 'ollama --version' para comprobarlo.";

        return new OllamaStatus(installed, ollama, ollamaVersion, detail);
    }

    public async Task<IReadOnlyList<OllamaModel>> ListOllamaModelsAsync(CancellationToken cancellationToken = default)
    {
        var ollama = FindOllama();
        if (ollama is null)
        {
            throw new AiException(BridgeErrorCodes.AiNotInstalled, "Ollama no está instalado.");
        }

        try
        {
            var result = await _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = ollama,
                    Arguments = AiArguments.OllamaListModels,
                    Timeout = ReadTimeout,
                    RequiresElevation = false,
                },
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return [];
            }

            return AiParser.ParseOllamaModels(result.StandardOutput);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "No se pudo listar los modelos de Ollama.");
            return [];
        }
    }

    private async Task<ProcessResult?> RunAsync(
        string path,
        string[] arguments,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        try
        {
            return await _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = path,
                    Arguments = arguments,
                    Timeout = timeout,
                    WorkingDirectory = workingDirectory,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProcessTimeoutException)
        {
            throw new AiException(BridgeErrorCodes.AiFailed, timeoutMessage);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "No se pudo ejecutar Python {Arguments}.", string.Join(' ', arguments));
            return null;
        }
    }
}

/// <summary>
/// Argumentos fijos para el CLI de Python + markItDown. Nunca viajan por el
/// puente: el frontend solo envía el id de operación o la ruta del archivo.
/// </summary>
internal static class AiArguments
{
    public static readonly string[] PythonVersion = ["--version"];
    public static readonly string[] PipShowMarkItDown = ["-m", "pip", "show", "markitdown"];
    public static readonly string[] PipInstallMarkItDown = ["-m", "pip", "install", "markitdown"];

    public static string[] MarkItDownConvert(string path) => ["-m", "markitdown", path];

    public static readonly string[] OllamaVersion = ["--version"];
    public static readonly string[] OllamaListModels = ["list"];
}

/// <summary>
/// Parsers de la salida de Python / pip / markItDown. Todos PUROS y testeables.
/// </summary>
internal static class AiParser
{
    public static string? ParsePythonVersion(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        // Ejemplo: "Python 3.11.9" o "Python 3.12.4"
        var match = System.Text.RegularExpressions.Regex.Match(output, @"Python\s+(\d+\.\d+\.\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string? ParseMarkItDownVersion(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        // Ejemplo: "Version: 0.1.0"
        var match = System.Text.RegularExpressions.Regex.Match(output, @"Version:\s*(\S+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string? PipDetail(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var lines = output.Split('\n');
        // Última línea significativa de pip (éxito o error)
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length > 0)
            {
                return line.Length > 200 ? line[..200] + "…" : line;
            }
        }
        return null;
    }

    public static string? ConvertDetail(string? stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return null;
        var lines = stderr.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length > 0 && !line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase))
            {
                return line.Length > 200 ? line[..200] + "…" : line;
            }
        }
        return stderr.Length > 200 ? stderr[..200] + "…" : stderr;
    }

    public static string? ParseOllamaVersion(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        // Ejemplo: "ollama version is 0.5.7"
        var match = System.Text.RegularExpressions.Regex.Match(output, @"version is\s+(\S+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    public static IReadOnlyList<OllamaModel> ParseOllamaModels(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return [];

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var models = new List<OllamaModel>();

        // Saltar la línea de encabezado: NAME   ID   SIZE   MODIFIED
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;

            // Formato: NAME ... SIZE ... MODIFIED
            var parts = line.Split(
                new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2) continue;

            var name = parts[0];
            var size = parts.Length >= 3 ? parts[parts.Length - 2] : null;
            var modified = parts.Length >= 4 ? string.Join(" ", parts.Skip(parts.Length - 2).Take(parts.Length - 2)) : null;

            models.Add(new OllamaModel(
                Name: name,
                Size: size,
                Modified: modified,
                Digest: null));
        }

        return models;
    }
}
