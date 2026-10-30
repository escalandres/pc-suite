namespace PCSuite.Contracts;

/// <summary>
/// DTOs de las operaciones ia.* (Task 038): detección/instalación de markItDown
/// y conversión de archivos a Markdown.
/// </summary>

/// <summary>Resultado de ia.detect: estado de Python y markItDown.</summary>
public sealed class AiStatusResult
{
    /// <summary>¿Está markItDown instalado y usable?</summary>
    public bool Installed { get; init; }

    /// <summary>Ruta resuelta del ejecutable de Python (ausente si no está instalado).</summary>
    public string? PythonPath { get; init; }

    /// <summary>Versión de markItDown (ausente si es desconocida).</summary>
    public string? MarkItDownVersion { get; init; }

    /// <summary>Detalle legible (ausente si todo OK).</summary>
    public string? Detail { get; init; }
}

/// <summary>Resultado de ia.install (igual que winget/clamav: structured outcome).</summary>
public sealed class AiMutationResult
{
    public bool Succeeded { get; init; }
    public int ExitCode { get; init; }
    /// <summary>True: corrió en proceso elevado (UAC aceptado).</summary>
    public bool Elevated { get; init; }
    public string? Detail { get; init; }
}

/// <summary>Resultado del job de ia.convert: archivo Markdown generado o error controlado.</summary>
public sealed class AiConvertResult
{
    public bool Succeeded { get; init; }
    public int ExitCode { get; init; }
    /// <summary>Ruta del archivo .md generado (ausente si falló).</summary>
    public string? OutputPath { get; init; }
    public string? Detail { get; init; }
}

/// <summary>Payload de ia.convert: SOLO la ruta del archivo a convertir.</summary>
public sealed class AiConvertPayload
{
    public string? Path { get; init; }
}

/// <summary>Resultado de ollama.detect: estado de Ollama.</summary>
public sealed class OllamaStatusResult
{
    /// <summary>¿Está Ollama instalado y usable?</summary>
    public bool Installed { get; init; }

    /// <summary>Ruta resuelta del ejecutable de Ollama (ausente si no está instalado).</summary>
    public string? OllamaPath { get; init; }

    /// <summary>Versión de Ollama (ausente si es desconocida).</summary>
    public string? Version { get; init; }

    /// <summary>Detalle legible (ausente si todo OK).</summary>
    public string? Detail { get; init; }
}

/// <summary>Un modelo de Ollama listado con `ollama list`.</summary>
public sealed class OllamaModelResult
{
    public string Name { get; init; } = string.Empty;
    public string? Size { get; init; }
    public string? Modified { get; init; }
    public string? Digest { get; init; }
}

/// <summary>Resultado de ollama.listModels: colección de modelos instalados.</summary>
public sealed class OllamaModelCollectionResult
{
    public List<OllamaModelResult> Models { get; init; } = [];
}

/// <summary>Payload de ollama.pullModel (futuro): nombre del modelo a descargar.</summary>
public sealed class OllamaPullPayload
{
    public string? ModelName { get; init; }
}
