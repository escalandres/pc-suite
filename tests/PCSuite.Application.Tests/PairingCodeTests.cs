using PCSuite.Infrastructure.LocalShare;

namespace PCSuite.Application.Tests;

/// <summary>
/// Tests de la derivación del código de comparación numérica (16.7 —
/// emparejamiento estilo Bluetooth).
/// </summary>
public class PairingCodeTests
{
    private const string ThumbA = "3b711f990a1148541c8f909ff2963fcb60a7c7d9253e1ef43dab88a18934ac8f";

    private const string ThumbB = "ea7a74de72e8710001e0619543b30a9ea464539c4313fdbcdc01bb6858a95a7b";

    [Fact]
    public void Derive_EsSimetrico()
    {
        // Ambos lados derivan sin coordinar el orden: mismo resultado.
        Assert.Equal(PairingCode.Derive(ThumbA, ThumbB), PairingCode.Derive(ThumbB, ThumbA));
    }

    [Fact]
    public void Derive_EsDeterminista()
    {
        Assert.Equal(PairingCode.Derive(ThumbA, ThumbB), PairingCode.Derive(ThumbA, ThumbB));
    }

    [Fact]
    public void Derive_FormatoSeisDigitos()
    {
        var code = PairingCode.Derive(ThumbA, ThumbB);

        Assert.Matches("^[0-9]{6}$", code);
    }

    [Fact]
    public void Derive_IgnoraMayusculasYEspacios()
    {
        // Las huellas siempre son hex minúscula, pero la normalización
        // evita códigos distintos por presentaciones equivalentes.
        Assert.Equal(
            PairingCode.Derive(ThumbA, ThumbB),
            PairingCode.Derive(ThumbB.ToUpperInvariant(), $" {ThumbA} "));
    }

    [Fact]
    public void Derive_HuellasDistintas_CodigoDistinto()
    {
        // Par de huellas distinto → código distinto: un MITM que presente
        // sus propios certificados no puede reproducir el código de la
        // pareja legítima (propiedad clave de la comparación numérica).
        var legit = PairingCode.Derive(ThumbA, ThumbB);
        var mitm = PairingCode.Derive(ThumbA, new string('f', 64));

        Assert.NotEqual(legit, mitm);
    }

    [Fact]
    public void Derive_HuellaVacia_Lanza()
    {
        Assert.Throws<ArgumentException>(() => PairingCode.Derive("", ThumbB));
        Assert.Throws<ArgumentException>(() => PairingCode.Derive(ThumbA, null!));
    }
}
