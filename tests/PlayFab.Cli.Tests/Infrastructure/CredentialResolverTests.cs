using PlayFab.Cli.Infrastructure;
using Xunit;

namespace PlayFab.Cli.Tests.Infrastructure;

[Collection("Environment")]
public class CredentialResolverTests : IDisposable
{
    private readonly CliConfig _emptyConfig = new();

    public CredentialResolverTests()
    {
        Environment.SetEnvironmentVariable("PLAYFAB_TITLE_ID", null);
        Environment.SetEnvironmentVariable("PLAYFAB_SECRET_KEY", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PLAYFAB_TITLE_ID", null);
        Environment.SetEnvironmentVariable("PLAYFAB_SECRET_KEY", null);
    }

    [Fact]
    public void Resolve_CliArgsTakePriority()
    {
        Environment.SetEnvironmentVariable("PLAYFAB_TITLE_ID", "ENV_TITLE");
        Environment.SetEnvironmentVariable("PLAYFAB_SECRET_KEY", "ENV_KEY");

        var result = CredentialResolver.Resolve("CLI_TITLE", "CLI_KEY", _emptyConfig);

        Assert.Equal("CLI_TITLE", result.TitleId);
        Assert.Equal("CLI_KEY", result.SecretKey);
    }

    [Fact]
    public void Resolve_EnvVarsFallbackWhenNoCliArgs()
    {
        Environment.SetEnvironmentVariable("PLAYFAB_TITLE_ID", "ENV_TITLE");
        Environment.SetEnvironmentVariable("PLAYFAB_SECRET_KEY", "ENV_KEY");

        var result = CredentialResolver.Resolve(null, null, _emptyConfig);

        Assert.Equal("ENV_TITLE", result.TitleId);
        Assert.Equal("ENV_KEY", result.SecretKey);
    }

    [Fact]
    public void Resolve_FallsBackToFile()
    {
        var fileConfig = new CliConfig { TitleId = "FILE_TITLE", SecretKey = "FILE_KEY" };

        var result = CredentialResolver.Resolve(null, null, fileConfig);

        Assert.Equal("FILE_TITLE", result.TitleId);
        Assert.Equal("FILE_KEY", result.SecretKey);
    }

    [Fact]
    public void Resolve_AllNull_ReturnsNulls()
    {
        var result = CredentialResolver.Resolve(null, null, _emptyConfig);

        Assert.Null(result.TitleId);
        Assert.Null(result.SecretKey);
    }

    [Fact]
    public void DescribeSource_IdentifiesCliArg()
    {
        Assert.Equal("CLI argument", CredentialResolver.DescribeSource("val", "PLAYFAB_TITLE_ID", null));
    }

    [Fact]
    public void DescribeSource_IdentifiesEnvVar()
    {
        Environment.SetEnvironmentVariable("PLAYFAB_TITLE_ID", "X");

        Assert.Equal("env: PLAYFAB_TITLE_ID", CredentialResolver.DescribeSource(null, "PLAYFAB_TITLE_ID", null));
    }

    [Fact]
    public void DescribeSource_IdentifiesFile()
    {
        Assert.Equal("config file", CredentialResolver.DescribeSource(null, "PLAYFAB_TITLE_ID", "stored"));
    }

    [Fact]
    public void DescribeSource_NotSet()
    {
        Assert.Equal("not set", CredentialResolver.DescribeSource(null, "PLAYFAB_TITLE_ID", null));
    }
}
