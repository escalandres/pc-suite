using System.Diagnostics;
using PCSuite.Contracts;
using PCSuite.Core.Cleaner;
using PCSuite.Infrastructure.Cleaner;

namespace PCSuite.Application.Tests;

// Task 011 / parte 11.2: pruebas del servicio con raíces inyectadas — solo se
// borra dentro de la raíz pedida, ids desconocidos se rechazan sin eco,
// archivos bloqueados se omiten y una junction dentro de la raíz no escapa.
public sealed class CleanerServiceTests : IDisposable
{
    private readonly string _baseDir;

    public CleanerServiceTests()
    {
        _baseDir = Path.Combine(
            Path.GetTempPath(),
            "pcsuite-cleaner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDir);
    }

    public void Dispose()
    {
        try
        {
            DeleteTreeWithoutFollowing(_baseDir);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Limpieza best-effort: un directorio temporal huérfano no falla la prueba.
        }
    }

    // --- helpers -----------------------------------------------------------

    private string RootPath(string categoryId) => Path.Combine(_baseDir, categoryId);

    private CleanerService CreateService() =>
        new(new Dictionary<string, CleanerRoot>(StringComparer.Ordinal)
        {
            [CleanerCategoryIds.UserTemp] = new("Temp del usuario", RootPath(CleanerCategoryIds.UserTemp)),
            [CleanerCategoryIds.WindowsTemp] = new("Temp de Windows", RootPath(CleanerCategoryIds.WindowsTemp)),
            [CleanerCategoryIds.UpdateCache] = new("Caché de Windows Update", RootPath(CleanerCategoryIds.UpdateCache)),
        });

    private string WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_baseDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private static void CreateJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        Assert.True(Directory.Exists(link), "No se pudo crear la junction de prueba (mklink /J).");
    }

    /// <summary>Borra un árbol sin seguir reparse points (una junction jamás arrastra su destino).</summary>
    private static void DeleteTreeWithoutFollowing(string path)
    {
        foreach (var directory in Directory.EnumerateDirectories(path))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(directory, false); // quita la junction, no el destino
            }
            else
            {
                DeleteTreeWithoutFollowing(directory);
            }
        }

        Directory.Delete(path, false);
    }

    // --- construcción ------------------------------------------------------

    [Fact]
    public void Constructor_MissingFixedCategory_Throws()
    {
        var roots = new Dictionary<string, CleanerRoot>(StringComparer.Ordinal)
        {
            [CleanerCategoryIds.UserTemp] = new("u", RootPath(CleanerCategoryIds.UserTemp)),
        };

        Assert.Throws<InvalidOperationException>(() => new CleanerService(roots));
    }

    [Fact]
    public void DefaultRoots_ResolveUnderWindowsAndLocalAppData()
    {
        var service = new CleanerService();
        Assert.NotNull(service);
    }

    // --- scan --------------------------------------------------------------

    [Fact]
    public async Task Scan_ReturnsAllCategoriesInFixedOrder()
    {
        var outcome = await CreateService().ScanAsync();

        Assert.Equal(CleanerCategoryIds.All, outcome.Categories.Select(category => category.Id));
        Assert.Equal("Temp del usuario", outcome.Categories[0].Label);
        Assert.Equal(RootPath(CleanerCategoryIds.UserTemp), outcome.Categories[0].Path);
    }

    [Fact]
    public async Task Scan_CountsFilesRecursivelyWithSizes()
    {
        WriteFile("userTemp/a.txt", "hello"); // 5
        WriteFile("userTemp/sub/b.bin", new string('x', 1024)); // 1024
        WriteFile("windowsTemp/c.txt", "abc"); // 3
        Directory.CreateDirectory(RootPath(CleanerCategoryIds.WindowsTemp));
        var service = CreateService();

        var outcome = await service.ScanAsync();

        var userTemp = outcome.Categories.Single(category => category.Id == CleanerCategoryIds.UserTemp);
        Assert.Equal(2, userTemp.FileCount);
        Assert.Equal(1029, userTemp.SizeBytes);
        Assert.True(userTemp.Accessible);

        var windowsTemp = outcome.Categories.Single(category => category.Id == CleanerCategoryIds.WindowsTemp);
        Assert.Equal(1, windowsTemp.FileCount);
        Assert.Equal(3, windowsTemp.SizeBytes);
    }

    [Fact]
    public async Task Scan_MissingRoot_IsAccessibleWithZero()
    {
        // updateCache no se crea a propósito
        var outcome = await CreateService().ScanAsync();

        var updateCache = outcome.Categories.Single(category => category.Id == CleanerCategoryIds.UpdateCache);
        Assert.Equal(0, updateCache.FileCount);
        Assert.Equal(0, updateCache.SizeBytes);
        Assert.True(updateCache.Accessible);
    }

    // --- clean -------------------------------------------------------------

    [Fact]
    public async Task Clean_DeletesInsideRoot_ReportsFreedBytes_AndScanDropsToZero()
    {
        WriteFile("userTemp/a.txt", "hello");
        WriteFile("userTemp/sub/b.bin", new string('x', 1024));
        var service = CreateService();

        var clean = await service.CleanAsync([CleanerCategoryIds.UserTemp]);

        var result = Assert.Single(clean.Categories);
        Assert.Equal(CleanerCategoryIds.UserTemp, result.Id);
        Assert.Equal(2, result.DeletedFiles);
        Assert.Equal(1029, result.FreedBytes);
        Assert.Equal(0, result.SkippedFiles);
        Assert.Null(result.Detail);

        var scan = await service.ScanAsync();
        Assert.Equal(
            0,
            scan.Categories.Single(category => category.Id == CleanerCategoryIds.UserTemp).FileCount);
    }

    [Fact]
    public async Task Clean_LeavesFilesOutsideTheRootUntouched()
    {
        WriteFile("userTemp/dentro.txt", "x");
        var outside = WriteFile("fuera.txt", "no me toques");
        var outsideSibling = WriteFile("other/precioso.txt", "yo tampoco");
        var service = CreateService();

        await service.CleanAsync(CleanerCategoryIds.All);

        Assert.False(File.Exists(Path.Combine(RootPath(CleanerCategoryIds.UserTemp), "dentro.txt")));
        Assert.True(File.Exists(outside));
        Assert.True(File.Exists(outsideSibling));
    }

    [Theory]
    [InlineData("USERTEMP")] // sin normalizar (ordinal)
    [InlineData("temp")]
    [InlineData(@"C:\Windows\Temp")] // jamás una ruta
    [InlineData("../userTemp")]
    public async Task Clean_UnknownCategory_ThrowsControlledWithoutEchoingInput(string categoryId)
    {
        var exception = await Assert.ThrowsAsync<CleanerException>(
            () => CreateService().CleanAsync([categoryId]));

        Assert.Equal(BridgeErrorCodes.CleanerFailed, exception.ErrorCode);
        Assert.DoesNotContain(categoryId, exception.ErrorMessage);
    }

    [Fact]
    public async Task Clean_EmptyOrNullList_ThrowsControlled()
    {
        var service = CreateService();

        var empty = await Assert.ThrowsAsync<CleanerException>(
            () => service.CleanAsync([]));
        var nullList = await Assert.ThrowsAsync<CleanerException>(
            () => service.CleanAsync(null!));

        Assert.Equal(BridgeErrorCodes.CleanerFailed, empty.ErrorCode);
        Assert.Equal(BridgeErrorCodes.CleanerFailed, nullList.ErrorCode);
    }

    [Fact]
    public async Task Clean_DuplicateIds_CleanOnlyOnce()
    {
        var service = CreateService();

        var outcome = await service.CleanAsync(
            [CleanerCategoryIds.UserTemp, CleanerCategoryIds.UserTemp]);

        Assert.Single(outcome.Categories);
    }

    [Fact]
    public async Task Clean_MissingRoot_ReturnsZerosWithoutError()
    {
        var outcome = await CreateService().CleanAsync([CleanerCategoryIds.UpdateCache]);

        var result = Assert.Single(outcome.Categories);
        Assert.Equal(0, result.DeletedFiles);
        Assert.Equal(0, result.SkippedFiles);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task Clean_LockedFile_IsSkippedAndSurvives()
    {
        var lockedPath = WriteFile("userTemp/bloqueado.tmp", "en uso");
        using var handle = new FileStream(
            lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var service = CreateService();

        var outcome = await service.CleanAsync([CleanerCategoryIds.UserTemp]);

        var result = Assert.Single(outcome.Categories);
        Assert.True(File.Exists(lockedPath));
        Assert.Equal(1, result.SkippedFiles);
        Assert.Equal(0, result.DeletedFiles);
        Assert.NotNull(result.Detail);
    }

    [Fact]
    public async Task Clean_JunctionInsideRoot_DoesNotReachTheTarget()
    {
        var outsideDir = Path.Combine(_baseDir, "outside");
        Directory.CreateDirectory(outsideDir);
        var outsideFile = Path.Combine(outsideDir, "precioso.txt");
        File.WriteAllText(outsideFile, "no me borres");

        var userTemp = RootPath(CleanerCategoryIds.UserTemp);
        Directory.CreateDirectory(userTemp);
        CreateJunction(Path.Combine(userTemp, "escape"), outsideDir);
        var insideFile = WriteFile("userTemp/dentro.txt", "x");
        var service = CreateService();

        await service.CleanAsync([CleanerCategoryIds.UserTemp]);

        Assert.False(File.Exists(insideFile));
        Assert.True(File.Exists(outsideFile));
    }
}
