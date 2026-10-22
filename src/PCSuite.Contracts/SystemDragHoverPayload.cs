namespace PCSuite.Contracts;

/// <summary>
/// Payload del evento system.dragHover (Task 026.4): estado y posición del
/// cursor durante un drag de archivos sobre PCSuite. Como el destino OLE es
/// del HOST (Task 026.3), la página no ve los eventos de drag del SO — este
/// evento es la única fuente de feedback visual en vivo.
/// </summary>
public sealed class SystemDragHoverPayload
{
    /// <summary>true mientras el drag de archivos está sobre la ventana; false al salir o al soltar.</summary>
    public bool Over { get; init; }

    /// <summary>X en píxeles FÍSICOS del cliente (el frontend la divide por devicePixelRatio para CSS px).</summary>
    public int? ClientX { get; init; }

    /// <summary>Y en píxeles FÍSICOS del cliente (el frontend la divide por devicePixelRatio para CSS px).</summary>
    public int? ClientY { get; init; }
}
