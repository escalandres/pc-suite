using PCSuite.Contracts;
using PCSuite.Core.Operations;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace PCSuite.Desktop.Bridge;

/// <summary>
/// system.writeClipboard — copia texto al portapapeles de Windows.
///
/// Por qué existe: el navigator.clipboard de Chromium en WebView2 exige el
/// permiso clipboard-write, que el host no concede (PermissionRequested no
/// se maneja) — writeText falla EN SILENCIO y los botones "Copiar" de la
/// app no hacían nada. El host es el dueño del portapapeles:
/// System.Windows.Forms.Clipboard lo escribe directo. Como
/// system.pickFiles, el router ejecuta la operación en el hilo UI
/// (OnWebMessageReceived) — el STAThread que Clipboard exige.
///
/// Contención: otro proceso puede retener el portapapeles un instante
/// (CLIPBRD_E_CANT_OPEN, ExternalException); se reintenta con esperas
/// ASÍNCRONAS para no congelar la UI antes de rendirse.
/// </summary>
public sealed class ClipboardWriteTextOperation : IOperation
{
    /// <summary>
    /// Tope defensivo: los mensajes de Local Share viajan limitados a 16 KB
    /// (Task 16.8) y los outputs de tools son acotados; 1 MB cubre todo lo
    /// razonable y evita que un payload gigante cruce el puente.
    /// </summary>
    internal const int MaxTextLength = 1_048_576;

    public string Id => OperationIds.SystemWriteClipboard;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct = default)
    {
        if (!context.Payload.HasValue)
        {
            return OperationResult.Fail("error", "El parámetro 'text' es obligatorio.");
        }

        SystemWriteClipboardPayload? payload;
        try
        {
            payload = context.Payload.Value.Deserialize<SystemWriteClipboardPayload>(BridgeJson.Options);
        }
        catch
        {
            return OperationResult.Fail("error", "Payload inválido.");
        }

        var text = payload?.Text;
        if (string.IsNullOrEmpty(text))
        {
            return OperationResult.Fail("error", "El parámetro 'text' es obligatorio.");
        }

        if (text.Length > MaxTextLength)
        {
            return OperationResult.Fail("error", "El texto supera el límite de 1 MB.");
        }

        // CLIPBRD_E_CANT_OPEN: el portapapeles está retenido por otro proceso
        // — el patrón clásico de Windows es reintentar brevemente.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return OperationResult.Ok();
            }
            catch (ExternalException) when (attempt < 4)
            {
                // Espera asíncrona: el hilo UI sigue vivo (continuación en
                // el SyncContext de WinForms — Clipboard sigue en hilo UI).
                await Task.Delay(120, ct).ConfigureAwait(true);
            }
        }
    }
}
