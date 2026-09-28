using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Cleaner;

namespace PCSuite.Application.Tests;

// Task 011 / parte 11.1: contrato cleaner.* — la lista fija de categorías,
// el payload del frontend (solo ids, nunca rutas) y el formato camelCase de
// los resultados que viajan por el puente.
public class CleanerContractTests
{
    [Fact]
    public void CategoryIds_AreTheFixedClosedSet()
    {
        Assert.Equal(
            new[] { "userTemp", "windowsTemp", "updateCache" },
            CleanerCategoryIds.All);
    }

    [Theory]
    [InlineData("userTemp", true)]
    [InlineData("windowsTemp", true)]
    [InlineData("updateCache", true)]
    [InlineData("USERTEMP", false)] // ordinal, sin normalizar
    [InlineData("C:\\Windows\\Temp", false)] // nunca una ruta
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsKnown_OnlyAcceptsTheFixedIds(string? id, bool expected)
    {
        Assert.Equal(expected, CleanerCategoryIds.IsKnown(id));
    }

    [Fact]
    public void CleanPayload_ParsesFrontendJson_CategoriesOnly()
    {
        var payload = JsonSerializer.Deserialize<CleanerCleanPayload>(
            """{"categories":["userTemp","updateCache"]}""",
            BridgeJson.Options);

        Assert.NotNull(payload?.Categories);
        Assert.Equal(2, payload!.Categories!.Count);
        Assert.Contains("userTemp", payload.Categories);
        Assert.Contains("updateCache", payload.Categories);
    }

    [Fact]
    public void CleanPayload_MissingCategories_YieldsNullServiceValidates()
    {
        var payload = JsonSerializer.Deserialize<CleanerCleanPayload>("{}", BridgeJson.Options);

        Assert.Null(payload?.Categories);
    }

    [Fact]
    public void ScanResult_UsesCamelCaseWireFormat()
    {
        var json = JsonSerializer.Serialize(
            new CleanerScanResult
            {
                Categories =
                [
                    new CleanerCategoryScanResult
                    {
                        Id = "userTemp",
                        Label = "Temp del usuario",
                        Path = @"C:\Users\x\AppData\Local\Temp",
                        FileCount = 3,
                        SizeBytes = 1024,
                        Accessible = true,
                    },
                ],
            },
            BridgeJson.Options);

        Assert.Contains("\"id\":\"userTemp\"", json);
        Assert.Contains("\"sizeBytes\":1024", json);
        Assert.Contains("\"accessible\":true", json);
        Assert.DoesNotContain("SizeBytes", json);
    }

    [Fact]
    public void CleanResult_UsesCamelCaseWireFormat()
    {
        var json = JsonSerializer.Serialize(
            new CleanerCleanResult
            {
                Categories =
                [
                    new CleanerCategoryCleanResult
                    {
                        Id = "windowsTemp",
                        FreedBytes = 512,
                        SkippedFiles = 9,
                        Detail = "requiere administrador",
                    },
                ],
            },
            BridgeJson.Options);

        Assert.Contains("\"freedBytes\":512", json);
        Assert.Contains("\"skippedFiles\":9", json);
        Assert.DoesNotContain("SkippedFiles", json);
    }

    [Fact]
    public void CleanerException_CarriesControlledCodeAndMessage()
    {
        var exception = new CleanerException(BridgeErrorCodes.CleanerFailed, "Categoria no valida.");

        Assert.Equal(BridgeErrorCodes.CleanerFailed, exception.ErrorCode);
        Assert.Equal("Categoria no valida.", exception.ErrorMessage);
        Assert.Equal("Categoria no valida.", exception.Message);
    }
}
