using PlayFab.Cli.Infrastructure;
using Xunit;

namespace PlayFab.Cli.Tests;

public class CliConfigTests
{
    [Fact]
    public void MergeWith_CliArgs_TakePriority()
    {
        var config = new CliConfig { TitleId = "from-file", SecretKey = "from-file" };
        var merged = config.MergeWith("from-cli", "from-cli-secret");

        Assert.Equal("from-cli", merged.TitleId);
        Assert.Equal("from-cli-secret", merged.SecretKey);
    }

    [Fact]
    public void MergeWith_NullArgs_FallsBackToFile()
    {
        var config = new CliConfig { TitleId = "from-file", SecretKey = "secret-file" };
        var merged = config.MergeWith(null, null);

        Assert.Equal("from-file", merged.TitleId);
        Assert.Equal("secret-file", merged.SecretKey);
    }

    [Fact]
    public void MergeWith_AllNull_ReturnsNulls()
    {
        var config = new CliConfig();
        var merged = config.MergeWith(null, null);

        Assert.Null(merged.TitleId);
        Assert.Null(merged.SecretKey);
    }
}
