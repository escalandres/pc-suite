using PCSuite.Contracts;
using PCSuite.Core.AI;
using PCSuite.Infrastructure.AI;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.Processes;

namespace PCSuite.Application.Tests;

/// <summary>Task 038: pruebas de los parsers puros de IA y validación de rutas.</summary>
public sealed class AiServiceTests
{
    [Theory]
    [InlineData("Python 3.11.9", "3.11.9")]
    [InlineData("Python 3.12.4", "3.12.4")]
    [InlineData("no version here", null)]
    public void ParsePythonVersion(string input, string? expected)
    {
        Assert.Equal(expected, AiParser.ParsePythonVersion(input));
    }

    [Theory]
    [InlineData("Version: 0.1.0", "0.1.0")]
    [InlineData("Name: markitdown\nVersion: 0.1.2", "0.1.2")]
    [InlineData("", null)]
    public void ParseMarkItDownVersion(string input, string? expected)
    {
        Assert.Equal(expected, AiParser.ParseMarkItDownVersion(input));
    }

    [Theory]
    [InlineData("ollama version is 0.5.7", "0.5.7")]
    [InlineData("ollama version is 0.3.0", "0.3.0")]
    [InlineData("", null)]
    public void ParseOllamaVersion(string input, string? expected)
    {
        Assert.Equal(expected, AiParser.ParseOllamaVersion(input));
    }

    [Fact]
    public void ParseOllamaModels_ParsesRows()
    {
        var input = "NAME            ID              SIZE    MODIFIED\nllama3:latest   abc123          4.7 GB  2 days ago\nmistral:latest  def456          4.1 GB  1 week ago\n";
        var models = AiParser.ParseOllamaModels(input);
        Assert.Equal(2, models.Count);
        Assert.Equal("llama3:latest", models[0].Name);
        Assert.Equal("mistral:latest", models[1].Name);
    }

    [Fact]
    public void ParseOllamaModels_Empty_ReturnsEmpty()
    {
        Assert.Empty(AiParser.ParseOllamaModels(""));
        Assert.Empty(AiParser.ParseOllamaModels(null));
    }

    [Fact]
    public void ValidateConvertPath_Null_Throws()
    {
        var svc = CreateService();
        Assert.Throws<AiException>(() => svc.ValidateConvertPath(null));
    }

    [Fact]
    public void ValidateConvertPath_Relative_Throws()
    {
        var svc = CreateService();
        Assert.Throws<AiException>(() => svc.ValidateConvertPath("relative/path.pdf"));
    }

    [Fact]
    public void ValidateConvertPath_NonExistent_Throws()
    {
        var svc = CreateService();
        Assert.Throws<AiException>(() => svc.ValidateConvertPath(Path.Combine(Path.GetTempPath(), "no-such-file-xyz.pdf")));
    }

    [Fact]
    public void ValidateConvertPath_Valid_ReturnsFullPath()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "ai-test-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(tempFile, "hello");
        try
        {
            var svc = CreateService();
            var result = svc.ValidateConvertPath(tempFile);
            Assert.Equal(Path.GetFullPath(tempFile), result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static AiService CreateService() =>
        new(new ExecutableLocator(), FakeProcessRunner.Returning(""));
}