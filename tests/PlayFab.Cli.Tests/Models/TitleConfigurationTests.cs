using PlayFab.Cli.Models;
using Xunit;

namespace PlayFab.Cli.Tests.Models;

public class TitleConfigurationTests : IDisposable
{
    private readonly string _tempDir;

    public TitleConfigurationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"playfab-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void LoadFromFile_ValidJson_Deserializes()
    {
        var path = WriteTempFile("""
            {
              "titleData": { "GameVersion": "1.0.0" }
            }
            """);

        var config = TitleConfiguration.LoadFromFile(path);

        Assert.NotNull(config.TitleData);
        Assert.Equal("1.0.0", config.TitleData!["GameVersion"]!.ToString());
    }

    [Fact]
    public void LoadFromFile_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            TitleConfiguration.LoadFromFile(Path.Combine(_tempDir, "missing.json")));
    }

    [Fact]
    public void LoadFromFile_WithComments_Accepted()
    {
        var path = WriteTempFile("""
            {
              // This is a comment
              "titleData": { "Key": "Value" }
            }
            """);

        var config = TitleConfiguration.LoadFromFile(path);

        Assert.NotNull(config.TitleData);
    }

    [Fact]
    public void Validate_EmptyConfig_ReturnsError()
    {
        var config = new TitleConfiguration();

        var errors = config.Validate();

        Assert.Single(errors);
        Assert.Contains("at least one section", errors[0]);
    }

    [Fact]
    public void Validate_ValidTitleData_NoErrors()
    {
        var path = WriteTempFile("""
            {
              "titleData": { "Key1": "Value1" }
            }
            """);

        var config = TitleConfiguration.LoadFromFile(path);
        var errors = config.Validate();

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_AllSectionsNull_ReturnsError()
    {
        var config = new TitleConfiguration();

        var errors = config.Validate();

        Assert.NotEmpty(errors);
    }

    private string WriteTempFile(string content)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }
}
