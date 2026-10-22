using System.Runtime.InteropServices;
using System.Text;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace PCSuite.Desktop.Bridge;

/// <summary>
/// Drop NATIVO de archivos del SO sobre el WebView2 (Task 026.3).
///
/// El problema: Chromium jamás entrega RUTAS absolutas de archivos
/// arrastrados a la página — el DataTransfer de JS solo ve nombre y
/// contenido ("File.path" es una extensión de Electron). Los handlers
/// onDrop del frontend nacen ciegos.
///
/// La solución: el HOST toma el destino OLE. Chromium registra su propio
/// IDropTarget en la ventana hija de contenido ("Chrome_WidgetWin_1"); se
/// revoca y se registra el nuestro, que recibe el CF_HDROP con las rutas
/// reales y las publica por callback — MainForm las emite como evento
/// system.filesDropped por el puente tipado. Las páginas que quieran las
/// rutas se suscriben; las que no, nada cambia.
///
/// Robustez: los nombres de clase de Chromium llevan años estables; si un
/// runtime futuro cambia el árbol de ventanas, Attach es un no-op y queda
/// el aviso honesto de la página (degradación elegante — nunca se rompe
/// nada). El registro ocurre en el hilo UI STA (requisito de OLE), desde
/// donde también llegan los callbacks.
/// </summary>
internal static class NativeFileDropTarget
{
    /// <summary>
    /// Registra el destino OLE sobre la ventana de contenido del WebView2.
    /// Devuelve false (no-op silencioso) si el árbol de ventanas no es el
    /// esperado — la página conserva su aviso honesto como fallback.
    /// </summary>
    public static bool Attach(IntPtr webViewHandle, Action<IReadOnlyList<string>> onFilesDropped)
    {
        var chrome = FindChromiumContentWindow(webViewHandle, depth: 0);
        if (chrome == IntPtr.Zero)
        {
            return false; // árbol cambió: sin DnD nativo (la página sigue con su aviso)
        }

        RevokeDragDrop(chrome);
        if (RegisterDragDrop(chrome, new DropTarget(onFilesDropped)) != 0)
        {
            return false; // no se pudo registrar: fallback de la página
        }

        return true;
    }

    // ------------------------------------------------------------------
    // Búsqueda de la ventana de contenido de Chromium (árbol estable hace
    // años). LECCIÓN del diagnóstico 2026-10-22: la ventana con el drop
    // target de Chromium es Chrome_WidgetWin_1 y NO es hijo directo del
    // control (el árbol real es control → _0 → _1): hay que buscarla
    // RECURSIVAMENTE por su nombre exacto antes de considerar el fallback.
    // ------------------------------------------------------------------

    private static IntPtr FindChromiumContentWindow(IntPtr parent, int depth)
    {
        if (depth > 6)
        {
            return IntPtr.Zero;
        }

        // La ventana de CONTENIDO (la dueña del drop target de Chromium):
        // búsqueda recursiva completa por su clase exacta.
        var content = FindByClassNameRecursive(parent, "Chrome_WidgetWin_1", depth);
        if (content != IntPtr.Zero)
        {
            return content;
        }

        // Fallback defensivo para árboles antiguos: el contenedor externo.
        return FindByClassNameRecursive(parent, "Chrome_WidgetWin_0", depth);
    }

    private static IntPtr FindByClassNameRecursive(IntPtr parent, string className, int depth)
    {
        if (depth > 6)
        {
            return IntPtr.Zero;
        }

        var direct = FindWindowEx(parent, IntPtr.Zero, className, null);
        if (direct != IntPtr.Zero)
        {
            return direct;
        }

        var child = IntPtr.Zero;
        while ((child = FindWindowEx(parent, child, null, null)) != IntPtr.Zero)
        {
            var found = FindByClassNameRecursive(child, className, depth + 1);
            if (found != IntPtr.Zero)
            {
                return found;
            }
        }

        return IntPtr.Zero;
    }

    // ------------------------------------------------------------------
    // P/Invoke
    // ------------------------------------------------------------------

    [DllImport("ole32.dll")]
    private static extern int RegisterDragDrop(IntPtr hwnd, IOleDropTarget dropTarget);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(
        IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM medium);

    /// <summary>POINTL de OLE (solo transita por la vtable; .NET 10 no lo expone en ComTypes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// IOleDropTarget de OLE. El orden de los métodos ES la vtable — jamás
    /// reordenar (DragEnter, DragOver, DragLeave, Drop).
    /// </summary>
    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleDropTarget
    {
        [PreserveSig] int DragEnter(ComTypes.IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);

        [PreserveSig] int DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect);

        [PreserveSig] int DragLeave();

        [PreserveSig] int Drop(ComTypes.IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
    }

    /// <summary>
    /// El destino OLE real: ofrece "copiar" cuando el drag trae archivos y
    /// al soltar extrae las rutas del CF_HDROP. Llega SIEMPRE en el hilo
    /// UI STA — el callback puede tocar el puente sin marshaling extra.
    /// </summary>
    [ComVisible(true)]
    private sealed class DropTarget : IOleDropTarget
    {
        private const short CfHdrop = 15; // CF_HDROP

        private const uint DropEffectNone = 0;
        private const uint DropEffectCopy = 1;

        private readonly Action<IReadOnlyList<string>> _onFiles;

        public DropTarget(Action<IReadOnlyList<string>> onFiles)
        {
            _onFiles = onFiles;
        }

        public int DragEnter(ComTypes.IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect)
        {
            // Solo aceptamos drags que traen archivos; el resto pasa de largo.
            pdwEffect = HasFiles(pDataObj) ? DropEffectCopy : DropEffectNone;
            return 0; // S_OK
        }

        public int DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect)
        {
            pdwEffect = DropEffectCopy;
            return 0;
        }

        public int DragLeave() => 0;

        public int Drop(ComTypes.IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect)
        {
            pdwEffect = DropEffectCopy;
            var paths = ExtractHdropPaths(pDataObj);
            if (paths.Count > 0)
            {
                _onFiles(paths);
            }

            return 0;
        }

        private static bool HasFiles(ComTypes.IDataObject data)
        {
            var format = HdropFormat();
            return data.QueryGetData(ref format) == 0;
        }

        private static ComTypes.FORMATETC HdropFormat() => new()
        {
            cfFormat = CfHdrop,
            ptd = IntPtr.Zero,
            dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = ComTypes.TYMED.TYMED_HGLOBAL,
        };

        private static IReadOnlyList<string> ExtractHdropPaths(ComTypes.IDataObject data)
        {
            if (!HasFiles(data))
            {
                return [];
            }

            var format = HdropFormat();
            data.GetData(ref format, out var medium);
            try
            {
                if (medium.tymed != ComTypes.TYMED.TYMED_HGLOBAL || medium.unionmember == IntPtr.Zero)
                {
                    return [];
                }

                var drop = medium.unionmember; // el HGLOBAL contiene el HDROP
                var count = DragQueryFile(drop, 0xFFFFFFFF, null, 0);
                var paths = new List<string>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    var needed = DragQueryFile(drop, i, null, 0);
                    if (needed == 0)
                    {
                        continue;
                    }

                    var builder = new StringBuilder((int)needed + 1);
                    if (DragQueryFile(drop, i, builder, (uint)builder.Capacity) > 0)
                    {
                        paths.Add(builder.ToString());
                    }
                }

                return paths;
            }
            finally
            {
                ReleaseStgMedium(ref medium);
            }
        }
    }
}
