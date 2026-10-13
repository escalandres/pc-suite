using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PCSuite.Infrastructure.LocalShare;

/// <summary>
/// Código de comparación numérica para el emparejamiento (modelo Bluetooth).
///
/// Ambos equipos derivan el MISMO código de 6 dígitos a partir de las DOS
/// huellas TLS de la conexión viva — la propia y la del par, tal y como se
/// presentaron en el handshake, NO de los anuncios multicast (falsificables).
/// Los usuarios comparan visualmente ambas pantallas: si un atacante se
/// interpone (MITM con certificados propios), cada lado ve huellas distintas
/// y los códigos difieren — el engaño se detecta igual que en Bluetooth LE
/// "numeric comparison".
/// </summary>
public static class PairingCode
{
    /// <summary>
    /// Deriva el código de 6 dígitos de dos huellas (hex minúscula).
    /// Simétrico: el orden de los argumentos no altera el resultado, así que
    /// ambos lados obtienen lo mismo sin coordinar quién es "A" y quién "B".
    /// </summary>
    public static string Derive(string thumbprintA, string thumbprintB)
    {
        if (string.IsNullOrEmpty(thumbprintA) || string.IsNullOrEmpty(thumbprintB))
        {
            throw new ArgumentException("Las huellas no pueden estar vacías.");
        }

        var ordered = new[] { thumbprintA.Trim().ToLowerInvariant(), thumbprintB.Trim().ToLowerInvariant() };
        Array.Sort(ordered, StringComparer.Ordinal);

        var material = Encoding.UTF8.GetBytes(string.Join("|", ordered));
        var hash = SHA256.HashData(material);

        // Big-endian explícito: independiente de la arquitectura de cada equipo.
        var value = BinaryPrimitives.ReadUInt32BigEndian(hash) % 1_000_000;
        return value.ToString("D6");
    }
}
