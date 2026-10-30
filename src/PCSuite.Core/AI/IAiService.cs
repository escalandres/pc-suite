namespace PCSuite.Core.AI;

/// <summary>
/// Fachada de markItDown (Task 038): PCSuite orquesta el CLI de Python, no es el
/// motor de conversión. El frontend envía payloads estructurados (la única ruta
/// es la del archivo a convertir, validada como ruta absoluta existente); los
/// argv siguen siendo arreglos fijos construidos en Infrastructure, sin shell
/// y nunca una línea "python -m markitdown …" arbitraria.
/// La conversión se ejecuta como JOB (puede tardar minutos en archivos grandes):
/// responde { jobId } al instante y el desenlace llega por job.event / job.get.
/// Sin tope de reloj (lección 024): el control es del usuario.
/// </summary>
public interface IAiService
{
    /// <summary>
    /// Detecta si Python y markItDown están instalados.
    /// Ejecuta `python --version` + `python -m pip show markitdown`.
    /// Solo lectura, sin elevación.
    /// </summary>
    Task<AiStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Instala markItDown con `python -m pip install markitdown`.
    /// Puede pedir UAC por operación si el entorno de Python lo requiere.
    /// </summary>
    Task<AiMutationOutcome> InstallAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida la ruta del archivo a convertir (raíz absoluta, existente, sin NUL)
    /// o lanza <see cref="AiException"/>.
    /// </summary>
    string ValidateConvertPath(string? path);

    /// <summary>
    /// Convierte un archivo a Markdown con `python -m markitdown <ruta>`.
    /// Devuelve el Markdown generado por stdout. Puede tardar mucho (OCR,
    /// transcripción de audio, PDFs grandes) → usar como JOB desde la op del puente.
    /// </summary>
    Task<AiConvertOutcome> ConvertAsync(
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detecta si Ollama está instalado.
    /// Ejecuta `ollama --version`. Solo lectura, sin elevación.
    /// </summary>
    Task<OllamaStatus> GetOllamaStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista los modelos instalados de Ollama con `ollama list`.
    /// Solo lectura, sin elevación.
    /// </summary>
    Task<IReadOnlyList<OllamaModel>> ListOllamaModelsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Estado de markItDown detectado (Python localizado + markItDown junto).</summary>
public sealed record AiStatus(
    bool Installed,
    string? PythonPath,
    string? PythonVersion,
    string? MarkItDownVersion,
    string? Detail);

/// <summary>Resultado de una mutación (install).</summary>
public sealed record AiMutationOutcome(
    bool Succeeded,
    int ExitCode,
    bool Elevated,
    string? Detail);

/// <summary>Resultado de una conversión a Markdown.</summary>
public sealed record AiConvertOutcome(
    bool Succeeded,
    int ExitCode,
    string? OutputPath,
    string? Detail);

/// <summary>Estado de Ollama detectado.</summary>
public sealed record OllamaStatus(
    bool Installed,
    string? OllamaPath,
    string? Version,
    string? Detail);

/// <summary>Un modelo instalado de Ollama.</summary>
public sealed record OllamaModel(
    string Name,
    string? Size,
    string? Modified,
    string? Digest);
