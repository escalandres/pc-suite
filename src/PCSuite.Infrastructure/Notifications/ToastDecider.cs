namespace PCSuite.Infrastructure.Notifications;

/// <summary>Tipos de evento que pueden producir un toast del sistema.</summary>
public enum ToastKind
{
    /// <summary>Un par quiere enviarte archivos (solicitud pendiente de aprobar).</summary>
    IncomingFiles,

    /// <summary>Un par quiere enviarte un mensaje (solicitud pendiente de aprobar).</summary>
    IncomingMessage,

    /// <summary>Un par quiere emparejarse con este equipo.</summary>
    PairRequested,

    /// <summary>Archivos recibidos y verificados (transferencia completada).</summary>
    FilesReceived,

    /// <summary>Mensaje recibido (transferencia completada) — con el texto.</summary>
    MessageReceived,

    /// <summary>Un escaneo de ClamAV halló amenazas.</summary>
    ThreatsFound,

    /// <summary>Hay una versión nueva disponible (check al arrancar).</summary>
    UpdateAvailable,
}

/// <summary>Entrada del decisor: el evento crudo + el foco de la app.</summary>
public sealed record ToastInput(
    ToastKind Kind,
    bool AppFocused,
    string? PeerName = null,
    int ItemCount = 0,
    long TotalBytes = 0,
    string? MessageText = null,
    int ThreatCount = 0,
    string? Version = null);

/// <summary>
/// Decisión de toast: mostrar (con título/cuerpo) o suprimir. Los textos son
/// SIEMPRE construidos aquí (jamás viajan datos crudos de eventos): sin
/// rutas absolutas, sin stack traces, sin contenido sin filtrar.
/// </summary>
public sealed record ToastDecision(bool Show, string? Title = null, string? Body = null)
{
    public static ToastDecision Suppressed { get; } = new(false);
}

/// <summary>
/// Decisor puro de notificaciones de Windows (Task 021): convierte eventos
/// de dominio en toasts del sistema. Regla de oro: si la app TIENE EL FOCO,
/// NUNCA se toastea — los banners internos (Activity center, Task 10.6) ya
/// están cubriendo el caso "el usuario está mirando". El toast existe para
/// el caso "el usuario está en OTRA cosa": recibiste un archivo, llegó un
/// mensaje, ClamAV halló algo, hay versión nueva.
///
/// El opt-out vive en Windows (Configuración → Notificaciones → PCSuite):
/// no duplicamos el toggle del SO.
/// </summary>
public static class ToastDecider
{
    /// <summary>Longitud máxima del preview de un mensaje en el toast.</summary>
    internal const int MessagePreviewMax = 120;

    public static ToastDecision Decide(ToastInput input)
    {
        // El usuario está mirando la app: nada que interrumpirle.
        if (input.AppFocused)
        {
            return ToastDecision.Suppressed;
        }

        var peer = string.IsNullOrWhiteSpace(input.PeerName)
            ? "Otro equipo"
            : input.PeerName!.Trim();

        return input.Kind switch
        {
            ToastKind.IncomingFiles => new ToastDecision(
                Show: true,
                Title: $"{peer} quiere enviarte {input.ItemCount} archivo(s)",
                Body: $"{FormatBytes(input.TotalBytes)} — acéptalo o recházalo en PCSuite"),

            ToastKind.IncomingMessage => new ToastDecision(
                Show: true,
                Title: $"{peer} te envió un mensaje",
                Body: "Acéptalo en PCSuite para leerlo"),

            ToastKind.PairRequested => new ToastDecision(
                Show: true,
                Title: $"{peer} quiere emparejarse con este equipo",
                Body: "Compara el código en ambas pantallas antes de aceptar"),

            ToastKind.FilesReceived => new ToastDecision(
                Show: true,
                Title: $"{input.ItemCount} archivo(s) recibidos",
                Body: $"De {peer} — verificados con SHA-256"),

            ToastKind.MessageReceived => new ToastDecision(
                Show: true,
                Title: $"Mensaje de {peer}",
                Body: PreviewMessage(input.MessageText ?? string.Empty)),

            ToastKind.ThreatsFound => new ToastDecision(
                Show: true,
                Title: $"ClamAV detectó {input.ThreatCount} amenaza(s)",
                Body: "Revisa el historial de escaneos en PCSuite"),

            ToastKind.UpdateAvailable => new ToastDecision(
                Show: true,
                Title: $"PCSuite {input.Version} disponible",
                Body: "Actualizar desde Ajustes › Actualizaciones"),

            _ => ToastDecision.Suppressed,
        };
    }

    /// <summary>Preview de una línea: saltos a espacios, truncado con elipsis.</summary>
    internal static string PreviewMessage(string text, int max = MessagePreviewMax)
    {
        var single = text.Replace("\r", " ").Replace("\n", " ").Trim();
        while (single.Contains("  ", StringComparison.Ordinal))
        {
            single = single.Replace("  ", " ", StringComparison.Ordinal);
        }

        return single.Length <= max ? single : single[..(max - 1)] + "…";
    }

    /// <summary>Formato legible de bytes (KB/MB/GB, sin dramatismo).</summary>
    internal static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var units = new[] { "KB", "MB", "GB" };
        double value = bytes;
        var unit = -1;
        do
        {
            value /= 1024;
            unit++;
        }
        while (value >= 1024 && unit < units.Length - 1);

        return $"{value:0.#} {units[unit]}";
    }
}
