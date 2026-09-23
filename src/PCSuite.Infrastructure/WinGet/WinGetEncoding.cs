using System.Text;

namespace PCSuite.Infrastructure.WinGet;

/// <summary>
/// Codificación de TODA la salida de winget. Verificado empíricamente
/// (v1.29, locale es-ES): winget escribe UTF-8 en la tubería ("ó" = C3 B3),
/// mientras el runtime decodifica con la codepage OEM por omisión — lo que
/// en es-ES convierte "Versión" en "Versi├│n" (dos glifos por acento) y
/// DESPLAZA las columnas "Disponible"/"4ª" de las tablas posicionales.
/// Cada ProcessRequest hacia winget declara esta codificación.
/// </summary>
internal static class WinGetEncoding
{
    public static Encoding Utf8 { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
