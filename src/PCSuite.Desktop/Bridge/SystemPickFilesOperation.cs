using System.Windows.Forms;
using PCSuite.Contracts;
using PCSuite.Core.Operations;

namespace PCSuite.Desktop.Bridge;

/// <summary>
/// system.pickFiles — diálogo NATIVO de Windows para elegir archivos
/// (multiselección) y devolver sus RUTAS ABSOLUTAS.
///
/// Por qué existe: el WebView2 no expone rutas locales en <input type=file>
/// ni en drag-and-drop ("File.path" es una extensión de Electron, no de
/// Chromium). Las herramientas que envían rutas al backend (Local Share)
/// necesitan el diálogo nativo: es un shell de escritorio, no un navegador.
///
/// Hilo: el router ejecuta la operación en el contexto del llamador y
/// OnWebMessageReceived dispara en el hilo UI — requisito de ShowDialog.
/// El modal bombea su propio bucle de mensajes: la UI sigue viva y la
/// respuesta del puente se completa al cerrar el diálogo. El frontend
/// DEBE usar un timeout generoso con esta operación.
/// </summary>
public sealed class SystemPickFilesOperation : IOperation
{
    private readonly IWin32Window? _owner;

    public SystemPickFilesOperation(IWin32Window? owner = null)
    {
        _owner = owner;
    }

    public string Id => OperationIds.SystemPickFiles;
    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        using var dialog = new OpenFileDialog
        {
            Multiselect = true,
            CheckFileExists = true,
            DereferenceLinks = true,
            Title = "Selecciona archivos para enviar",
        };

        var result = _owner is null ? dialog.ShowDialog() : dialog.ShowDialog(_owner);

        var dto = new SystemPickFilesResult
        {
            Cancelled = result != DialogResult.OK,
            Paths = result == DialogResult.OK ? dialog.FileNames : [],
        };

        return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(dto)));
    }
}
