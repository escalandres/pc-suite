using System.Windows.Forms;
using PCSuite.Contracts;
using PCSuite.Core.Operations;

namespace PCSuite.Desktop.Bridge;

/// <summary>
/// system.pickFolder — diálogo NATIVO de Windows para elegir UNA carpeta.
///
/// Por qué existe (Task 026): el escaneo personalizado de ClamAV necesita la
/// RUTA ABSOLUTA de una carpeta y el WebView no puede proveerla (igual que
/// system.pickFiles: los input HTML solo dan nombres sueltos y el
/// drag-and-drop del navegador no entrega rutas).
///
/// El "diálogo moderno" de carpetas: el FolderBrowserDialog de WinForms
/// sigue siendo el árbol clásico de XP. El truco estándar para el picker
/// MODERNO (el mismo diálogo "Abrir" de Windows 10/11) es OpenFileDialog
/// sin validar nombres con un centinela en el cuadro de nombre: al "abrir"
/// una carpeta, FileName queda con su ruta. Si el centinela sobrevive
/// dentro de la carpeta, la ruta real es su carpeta contenedora.
///
/// Hilo: igual que system.pickFiles — el router ejecuta la operación en el
/// hilo UI (OnWebMessageReceived), requisito de ShowDialog. El modal bombea
/// su propio bucle: la UI sigue viva y la respuesta del puente se completa
/// al cerrar el diálogo. El frontend DEBE usar un timeout generoso.
/// </summary>
public sealed class SystemPickFolderOperation : IOperation
{
    /// <summary>
    /// Nombre "fantasma" que ocupa el cuadro de nombre: hace que el diálogo
    /// de abrir archivos acepte la carpeta actual al pulsar "Abrir".
    /// </summary>
    private const string SentinelFileName = "Seleccionar esta carpeta";

    private readonly IWin32Window? _owner;

    public SystemPickFolderOperation(IWin32Window? owner = null)
    {
        _owner = owner;
    }

    public string Id => OperationIds.SystemPickFolder;
    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        using var dialog = new OpenFileDialog
        {
            ValidateNames = false, // el "archivo" centinela no existe
            CheckFileExists = false,
            DereferenceLinks = true,
            FileName = SentinelFileName,
            Title = "Selecciona una carpeta",
        };

        var result = _owner is null ? dialog.ShowDialog() : dialog.ShowDialog(_owner);
        if (result != DialogResult.OK)
        {
            return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(
                new SystemPickFolderResult { Cancelled = true })));
        }

        var chosen = dialog.FileName;
        if (!Directory.Exists(chosen))
        {
            // El centinela quedó "dentro" de la carpeta abierta: la ruta
            // real es su carpeta contenedora.
            var parent = Path.GetDirectoryName(chosen);
            if (parent is null || !Directory.Exists(parent))
            {
                return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(
                    new SystemPickFolderResult { Cancelled = true })));
            }

            chosen = parent;
        }

        return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(
            new SystemPickFolderResult { Cancelled = false, Path = chosen })));
    }
}
