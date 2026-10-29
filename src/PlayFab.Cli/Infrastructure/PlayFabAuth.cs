using PlayFab;
using PlayFab.AuthenticationModels;

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

    /// <summary>Resolves credentials and obtains an entity token, required for Progression API calls.</summary>
    public static async Task<ResolvedCredentials> RequireEntityCredentialsAsync(string? cliTitleId, string? cliSecretKey)
    {
        var creds = RequireCredentials(cliTitleId, cliSecretKey);

        var result = await PlayFabAuthenticationAPI.GetEntityTokenAsync(new GetEntityTokenRequest());
        if (result.Error is not null)
            throw new InvalidOperationException($"Entity authentication failed: {result.Error.ErrorMessage}");

        return creds;
    }
}
