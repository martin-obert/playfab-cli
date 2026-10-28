using PlayFab;

namespace PlayFab.Cli.Infrastructure;

public static class PlayFabAuth
{
    public static ResolvedCredentials RequireCredentials(string? cliTitleId, string? cliSecretKey)
    {
        var creds = CredentialResolver.Resolve(cliTitleId, cliSecretKey);

        if (string.IsNullOrWhiteSpace(creds.TitleId))
            throw new InvalidOperationException(
                "Title ID is required. Use --title-id, PLAYFAB_TITLE_ID, or 'playfab config set'.");

        if (string.IsNullOrWhiteSpace(creds.SecretKey))
            throw new InvalidOperationException(
                "Secret Key is required. Use --secret-key, PLAYFAB_SECRET_KEY, or 'playfab config set'.");

        PlayFabSettings.staticSettings.TitleId = creds.TitleId;
        PlayFabSettings.staticSettings.DeveloperSecretKey = creds.SecretKey;
        return creds;
    }
}
