using PlayFab.Cli.Infrastructure;
using Xunit;

namespace PlayFab.Cli.Tests.Infrastructure;

public class CliConfigTests : IDisposable
{
    private readonly string _tempDir;

    public CliConfigTests()
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
    public void Load_NoFile_ReturnsEmpty()
    {
        var config = CliConfig.Load(_tempDir);

        Assert.Null(config.TitleId);
        Assert.Null(config.SecretKey);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var config = CliConfig.Load(_tempDir);
        config.TitleId = "ABCD";
        config.SecretKey = "SECRET123";
        config.Save();

        var loaded = CliConfig.Load(_tempDir);

        Assert.Equal("ABCD", loaded.TitleId);
        Assert.Equal("SECRET123", loaded.SecretKey);
    }

    [Fact]
    public void Save_PartialUpdate_PreservesExistingValues()
    {
        var config = CliConfig.Load(_tempDir);
        config.TitleId = "FIRST";
        config.SecretKey = "KEY1";
        config.Save();

        var updated = CliConfig.Load(_tempDir);
        updated.TitleId = "SECOND";
        updated.Save();

        var final = CliConfig.Load(_tempDir);

        Assert.Equal("SECOND", final.TitleId);
        Assert.Equal("KEY1", final.SecretKey);
    }
}
