namespace PlayFab.Cli.Infrastructure;

public sealed record ResolvedCredentials(string? TitleId, string? SecretKey);

public static class CredentialResolver
{
    public static ResolvedCredentials Resolve(string? cliTitleId, string? cliSecretKey, CliConfig? fileConfig = null)
    {
        fileConfig ??= CliConfig.Load();

        var titleId = cliTitleId
            ?? Environment.GetEnvironmentVariable("PLAYFAB_TITLE_ID")
            ?? fileConfig.TitleId;

        var secretKey = cliSecretKey
            ?? Environment.GetEnvironmentVariable("PLAYFAB_SECRET_KEY")
            ?? fileConfig.SecretKey;

        return new ResolvedCredentials(titleId, secretKey);
    }

    public static string DescribeSource(string? cliArg, string envVarName, string? fileValue)
    {
        if (cliArg is not null) return "CLI argument";
        if (Environment.GetEnvironmentVariable(envVarName) is not null) return $"env: {envVarName}";
        if (fileValue is not null) return "config file";
        return "not set";
    }
}
