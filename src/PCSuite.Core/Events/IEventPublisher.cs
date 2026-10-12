namespace PCSuite.Core.Events;

/// <summary>
/// Publica eventos hacia el frontend (WebView2). Implementado por la capa Desktop
/// para evitar dependencias circulares (Infrastructure no referencia Desktop).
/// </summary>
public interface IEventPublisher
{
    /// <summary>Envía un evento tipado al frontend.</summary>
    void SendEvent<TPayload>(string eventId, TPayload payload);
}