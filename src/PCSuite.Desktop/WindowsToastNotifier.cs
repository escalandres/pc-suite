using Microsoft.Toolkit.Uwp.Notifications;
using PCSuite.Infrastructure.Notifications;

namespace PCSuite.Desktop;

/// <summary>
/// Emisor de toasts del sistema (Task 021): lleva los eventos de dominio al
/// Centro de Notificaciones de Windows. El DECISOR (qué toastear y con qué
/// texto) vive en Infrastructure y está testeado; esta clase solo RENDERIZA.
///
/// Sin empaquetado: ToastNotificationManagerCompat registra el AppUserModelID
/// y el atajo del Start Menu automáticamente en el primer toast (requisito de
/// Windows para atribuir la notificación a PCSuite con su icono). El opt-out
/// del usuario vive en Windows: Configuración → Sistema → Notificaciones →
/// PCSuite — no duplicamos el toggle del SO.
/// </summary>
internal sealed class WindowsToastNotifier : IDisposable
{
    private readonly Func<bool> _isAppFocused;
    private readonly Action _bringToFront;
    private bool _disposed;

    public WindowsToastNotifier(Func<bool> isAppFocused, Action bringToFront)
    {
        _isAppFocused = isAppFocused;
        _bringToFront = bringToFront;
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
    }

    /// <summary>
    /// Decide y (si procede) muestra el toast. Devuelve la decisión para que
    /// el caller pueda registrarla (diagnóstico en logs, Activity futuro).
    /// </summary>
    public ToastDecision Show(ToastInput input)
    {
        if (_disposed)
        {
            return ToastDecision.Suppressed;
        }

        var decision = ToastDecider.Decide(input with
        {
            // El foco lo pregunta AQUÍ (en el momento del evento), no al crear.
            AppFocused = _isAppFocused(),
        });

        if (!decision.Show)
        {
            return decision;
        }

        try
        {
            new ToastContentBuilder()
                .AddText(decision.Title ?? "PCSuite")
                .AddText(decision.Body ?? string.Empty)
                .Show();
        }
        catch
        {
            // El toast es best-effort: si Windows lo rechaza (política,
            // servidor de toasts ausente, focus assist), la app sigue igual.
            // El banner interno ya cubrió el caso si el usuario miraba.
        }

        return decision;
    }

    /// <summary>Clic en el toast → traer PCSuite al frente. Si el toast lo
    /// activa siendo la app YA cerrada, Windows relanza PCSuite.exe (sin
    /// args): la app abre normal — suficiente.</summary>
    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        try
        {
            _bringToFront();
        }
        catch
        {
            // best-effort
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
        // Uninstall() NO: es para DESINSTALADORES (borra el AUMID). Aquí solo
        // despedirnos del evento.
    }
}
