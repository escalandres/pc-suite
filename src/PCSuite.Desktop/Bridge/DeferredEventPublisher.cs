using PCSuite.Core.Events;

namespace PCSuite.Desktop.Bridge;

/// <summary>
/// Publisher de eventos diferido: los servicios lo reciben EN EL ACTO y el
/// WebViewBridge se conecta al final del arranque (el router necesita los
/// servicios y el bridge necesita el router — antes este orden hacía que
/// ClamAV recibiera un bridge aún null y los eventos de progreso jamás
/// llegaran a la UI). SendEvent antes de Attach es un no-op.
/// </summary>
public sealed class DeferredEventPublisher : IEventPublisher
{
    private WebViewBridge? _target;

    public void Attach(WebViewBridge bridge) => _target = bridge;

    public void SendEvent<TPayload>(string eventId, TPayload payload) =>
        _target?.SendEvent(eventId, payload);
}
