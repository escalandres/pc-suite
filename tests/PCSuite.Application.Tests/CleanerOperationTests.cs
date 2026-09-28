using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Contracts;
using PCSuite.Core.Cleaner;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.Cleaner;
using PCSuite.Infrastructure.Operations;

namespace PCSuite.Application.Tests;

/// <summary>
/// Las 2 operaciones cleaner.* por el puente (Task 011): contrato camelCase,
/// scan de payload cero (payload basura ignorado: jamás influye en qué se
/// lee), clean con {"categories":[ids]} de la lista fija, fallos controlados
/// (MALFORMED_MESSAGE/CLEANER_FAILED, nunca INTERNAL_ERROR ni stack traces,
/// sin eco de la entrada) y RequiresElevation falso en ambas.
/// </summary>
public sealed class CleanerOperationTests : IDisposable
{
    private readonly string _baseDir;
    private readonly CommandRouter _router;

    public CleanerOperationTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "pcsuite-cleaner-ops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDir);

        var service = new CleanerService(new Dictionary<string, CleanerRoot>(StringComparer.Ordinal)
        {
            [CleanerCategoryIds.UserTemp] = new("Temp del usuario", Path.Combine(_baseDir, "userTemp")),
            [CleanerCategoryIds.WindowsTemp] = new("Temp de Windows", Path.Combine(_baseDir, "windowsTemp")),
            [CleanerCategoryIds.UpdateCache] = new("Caché de Windows Update", Path.Combine(_baseDir, "updateCache")),
        });

        _router = new CommandRouter(
            new IOperation[]
            {
                new CleanerScanOperation(service),
                new CleanerCleanOperation(service),
            },
            NullLogger<CommandRouter>.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_baseDir, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Limpieza best-effort del directorio temporal de prueba.
        }
    }

    // ------------------------------------------------------------------
    // Contrato camelCase
    // ------------------------------------------------------------------

    [Fact]
    public async Task Scan_ReturnsFixedCategories_CamelCase()
    {
        var target = Path.Combine(_baseDir, "userTemp", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "hello"); // 5 bytes

        var response = await Send(_router, "s1", OperationIds.CleanerScan);

        Assert.True(response.Success);
        var categories = response.Result!.Value.GetProperty("categories");
        Assert.Equal(3, categories.GetArrayLength());

        var first = categories[0];
        Assert.Equal(CleanerCategoryIds.UserTemp, first.GetProperty("id").GetString());
        Assert.Equal("Temp del usuario", first.GetProperty("label").GetString());
        Assert.Equal(1, first.GetProperty("fileCount").GetInt64());
        Assert.Equal(5, first.GetProperty("sizeBytes").GetInt64());
        Assert.True(first.GetProperty("accessible").GetBoolean());
        Assert.Equal("windowsTemp", categories[1].GetProperty("id").GetString());
        Assert.Equal("updateCache", categories[2].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Scan_JunkPayload_IsIgnored_CannotInfluenceWhatIsRead()
    {
        // El frontend no puede inyectar rutas ni argumentos en el escaneo:
        // cleaner.scan es de payload cero y todo lo demás se ignora.
        var response = await Send(
            _router, "s2", OperationIds.CleanerScan, """{"path":"C:\\evil","args":["--bell"]}""");

        Assert.True(response.Success);
        var categories = response.Result!.Value.GetProperty("categories");
        Assert.Equal(CleanerCategoryIds.All.Count, categories.GetArrayLength());
        foreach (var category in categories.EnumerateArray())
        {
            Assert.Contains(_baseDir, category.GetProperty("path").GetString()!);
        }
    }

    [Fact]
    public async Task Clean_ParsesCategories_DeletesAndReports_CamelCase()
    {
        var target = Path.Combine(_baseDir, "userTemp", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "hello");

        var response = await Send(
            _router, "c1", OperationIds.CleanerClean, """{"categories":["userTemp"]}""");

        Assert.True(response.Success);
        var category = Assert.Single(response.Result!.Value.GetProperty("categories").EnumerateArray());
        Assert.Equal(CleanerCategoryIds.UserTemp, category.GetProperty("id").GetString());
        Assert.Equal(1, category.GetProperty("deletedFiles").GetInt64());
        Assert.Equal(5, category.GetProperty("freedBytes").GetInt64());
        Assert.Equal(0, category.GetProperty("skippedFiles").GetInt64());
        Assert.False(File.Exists(target));
    }

    // ------------------------------------------------------------------
    // Fallos controlados (nunca INTERNAL_ERROR ni eco de la entrada)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Clean_MissingPayload_IsControlledCleanerFailed()
    {
        var response = await Send(_router, "c2", OperationIds.CleanerClean);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.CleanerFailed, response.Error!.Code);
        Assert.DoesNotContain("System.", response.Error.Message);
    }

    [Theory]
    [InlineData("\"no soy objeto\"")]
    [InlineData("123")]
    [InlineData("[\"userTemp\"]")]
    public async Task Clean_NonObjectPayload_IsMalformedMessage(string payloadJson)
    {
        var response = await Send(_router, "c3", OperationIds.CleanerClean, payloadJson);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, response.Error!.Code);
        Assert.NotEqual(BridgeErrorCodes.InternalError, response.Error.Code);
    }

    [Theory]
    [InlineData("C:/Windows/System32")] // jamás una ruta
    [InlineData("../../userTemp")] // ni traversal
    [InlineData("USERTEMP")] // sin normalizar (ordinal)
    [InlineData("no-such-id")]
    public async Task Clean_UnknownCategory_IsControlledWithoutEchoingInput(string categoryId)
    {
        var response = await Send(
            _router,
            "c4",
            OperationIds.CleanerClean,
            $$"""{"categories":["{{categoryId}}"]}""");

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.CleanerFailed, response.Error!.Code);
        Assert.DoesNotContain(categoryId, response.Error.Message);
        Assert.DoesNotContain("   at ", response.Error.Message); // nunca stack traces
    }

    [Fact]
    public async Task UnknownCleanerOperation_IsOperationNotFound()
    {
        var response = await Send(_router, "x1", "cleaner.deleteEverything");

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.OperationNotFound, response.Error!.Code);
    }

    // ------------------------------------------------------------------
    // Metadato de elevación
    // ------------------------------------------------------------------

    [Fact]
    public void BothOperations_DoNotRequireElevation()
    {
        var operations = new IOperation[]
        {
            new CleanerScanOperation(new CleanerService()),
            new CleanerCleanOperation(new CleanerService()),
        };

        Assert.All(operations, operation => Assert.False(operation.RequiresElevation));
        Assert.Equal(
            new[] { OperationIds.CleanerScan, OperationIds.CleanerClean },
            operations.Select(operation => operation.Id));
    }

    private static Task<BridgeResponse> Send(
        CommandRouter router,
        string id,
        string operation,
        string? payloadJson = null)
    {
        var payload = payloadJson is null ? string.Empty : $",\"payload\":{payloadJson}";
        return router.RouteAsync(
            $"{{\"id\":\"{id}\",\"operation\":\"{operation}\"{payload}}}");
    }
}
