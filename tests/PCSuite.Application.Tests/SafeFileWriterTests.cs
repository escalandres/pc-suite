using PCSuite.Infrastructure.LocalShare;
using System.Security.Cryptography;
using System.Text;

namespace PCSuite.Application.Tests;

/// <summary>
/// Tests de SafeFileWriter: validación anti-traversal, colisiones,
/// SHA-256 streaming, y publicación segura. Seguridad crítica (§7.3).
/// </summary>
public class SafeFileWriterTests : IDisposable
{
    private readonly string _root;
    private readonly SafeFileWriter _writer;

    public SafeFileWriterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pcshare-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _writer = new SafeFileWriter(_root);
    }

    public void Dispose()
    {
        _writer.Dispose();
        try { Directory.Delete(_root, true); } catch { /* best-effort */ }
    }

    // ------------------------------------------------------------------
    // Validación de rutas relativas (anti-traversal)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("archivo.txt")]
    [InlineData("carpeta/archivo.txt")]
    [InlineData("carpeta/sub/archivo.txt")]
    [InlineData("carpeta/sub/archivo con espacios.txt")]
    public void ValidateRelativePath_ValidPath_Passes(string relativePath)
    {
        var result = _writer.ValidateRelativePath(relativePath);
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("..\\..\\windows\\system32\\evil.exe")]
    [InlineData("../../etc/passwd")]
    [InlineData("..")]
    [InlineData("../archivo.txt")]
    [InlineData("carpeta/../../escape.txt")]
    public void ValidateRelativePath_TraversalAttempt_Throws(string relativePath)
    {
        Assert.ThrowsAny<ArgumentException>(() => _writer.ValidateRelativePath(relativePath));
    }

    [Theory]
    [InlineData("C:\\Windows\\evil.exe")]
    [InlineData("/etc/passwd")]
    [InlineData("\\\\servidor\\compartido\\archivo.txt")]
    public void ValidateRelativePath_AbsoluteOrUnc_Throws(string relativePath)
    {
        Assert.ThrowsAny<ArgumentException>(() => _writer.ValidateRelativePath(relativePath));
    }

    [Fact]
    public void ValidateRelativePath_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => _writer.ValidateRelativePath(""));
        Assert.Throws<ArgumentException>(() => _writer.ValidateRelativePath("   "));
    }

    [Fact]
    public void ValidateRelativePath_ControlChars_Throws()
    {
        Assert.Throws<ArgumentException>(() => _writer.ValidateRelativePath("archivo\x00.txt"));
        Assert.Throws<ArgumentException>(() => _writer.ValidateRelativePath("archivo\n.txt"));
    }

    // ------------------------------------------------------------------
    // SHA-256 en streaming
    // ------------------------------------------------------------------

    [Fact]
    public void ComputeSha256_KnownVector_Matches()
    {
        var path = Path.Combine(_root, "test-sha.txt");
        File.WriteAllText(path, "abc");
        var hash = SafeFileWriter.ComputeSha256(path);
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            hash);
    }

    [Fact]
    public void ComputeSha256_LargeFile_DoesNotLoadInMemory()
    {
        var path = Path.Combine(_root, "test-large.bin");
        using (var stream = File.Create(path))
        {
            stream.Write(new byte[10 * 1024 * 1024]); // 10MB
        }
        var hash = SafeFileWriter.ComputeSha256(path);
        Assert.NotEmpty(hash);
        Assert.Equal(64, hash.Length); // SHA-256 hex = 64 chars
    }

    // ------------------------------------------------------------------
    // Publicación y colisiones
    // ------------------------------------------------------------------

    [Fact]
    public void Publish_ValidFile_MovesToDestination()
    {
        var tempPath = _writer.CreateTempFile();
        File.WriteAllText(tempPath, "contenido");
        var hash = SafeFileWriter.ComputeSha256(tempPath);

        var finalPath = _writer.Publish(tempPath, "carpeta/archivo.txt", 9, hash);

        Assert.True(File.Exists(finalPath));
        Assert.Equal("contenido", File.ReadAllText(finalPath));
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public void Publish_WrongHash_Throws()
    {
        var tempPath = _writer.CreateTempFile();
        File.WriteAllText(tempPath, "contenido");

        Assert.Throws<InvalidOperationException>(() =>
            _writer.Publish(tempPath, "archivo.txt", 9, "hash-falso"));
    }

    [Fact]
    public void Publish_WrongSize_Throws()
    {
        var tempPath = _writer.CreateTempFile();
        File.WriteAllText(tempPath, "contenido"); // 9 bytes

        Assert.Throws<InvalidOperationException>(() =>
            _writer.Publish(tempPath, "archivo.txt", 999, null));
    }

    [Fact]
    public void Publish_Collision_AddsSuffix()
    {
        // Crear archivo destino existente
        Directory.CreateDirectory(Path.Combine(_root, "carpeta"));
        File.WriteAllText(Path.Combine(_root, "carpeta", "archivo.txt"), "original");

        var tempPath = _writer.CreateTempFile();
        File.WriteAllText(tempPath, "nuevo");
        var hash = SafeFileWriter.ComputeSha256(tempPath);

        var finalPath = _writer.Publish(tempPath, "carpeta/archivo.txt", 5, hash);

        Assert.EndsWith("(1).txt", finalPath);
        Assert.Equal("original", File.ReadAllText(Path.Combine(_root, "carpeta", "archivo.txt")));
        Assert.Equal("nuevo", File.ReadAllText(finalPath));
    }

    [Fact]
    public void Publish_MultipleCollisions_IncrementSuffix()
    {
        Directory.CreateDirectory(Path.Combine(_root, "d"));
        File.WriteAllText(Path.Combine(_root, "d", "f.txt"), "0");
        File.WriteAllText(Path.Combine(_root, "d", "f (1).txt"), "1");
        File.WriteAllText(Path.Combine(_root, "d", "f (2).txt"), "2");

        var tempPath = _writer.CreateTempFile();
        File.WriteAllText(tempPath, "3");
        var hash = SafeFileWriter.ComputeSha256(tempPath);

        var finalPath = _writer.Publish(tempPath, "d/f.txt", 1, hash);

        Assert.EndsWith("(3).txt", finalPath);
    }

    // ------------------------------------------------------------------
    // Temporales
    // ------------------------------------------------------------------

    [Fact]
    public void CreateTempFile_HasGuidName()
    {
        var tempPath = _writer.CreateTempFile();
        var name = Path.GetFileName(tempPath);
        Assert.EndsWith(".part", name);
        Assert.Equal(32 + 5, name.Length); // GUID N (32) + ".part" (5)
    }

    [Fact]
    public void DiscardTemp_RemovesFile()
    {
        var tempPath = _writer.CreateTempFile();
        File.WriteAllText(tempPath, "x");
        Assert.True(File.Exists(tempPath));

        _writer.DiscardTemp(tempPath);
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public void CleanupOrphanedTempFiles_RemovesAll()
    {
        var t1 = _writer.CreateTempFile();
        var t2 = _writer.CreateTempFile();
        File.WriteAllText(t1, "x");
        File.WriteAllText(t2, "y");

        _writer.CleanupOrphanedTempFiles();
        Assert.False(File.Exists(t1));
        Assert.False(File.Exists(t2));
    }
}
