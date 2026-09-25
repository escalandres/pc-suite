using System.Text;

namespace PCSuite.Infrastructure.Wsl;

/// <summary>
/// Codificación de wsl.exe: UTF-16LE sin BOM (CodePage 1200). El CLI real
/// escribe así (incluso en es-ES); declararla en la petición cubre tanto la
/// salida como los errores (wsl escribe errores por stdout). Los fixtures del
/// repo son UTF-8 y los leen los tests con File.ReadAllText (UTF-8 por defecto).
/// </summary>
internal static class WslEncoding
{
    public static Encoding Utf16Le { get; } = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
}
