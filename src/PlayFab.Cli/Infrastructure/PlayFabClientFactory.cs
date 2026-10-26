using PlayFab;
using PlayFab.AuthenticationModels;

namespace PlayFab.Cli.Infrastructure;

/// <summary>Sets up PlayFab SDK static settings and authenticates.</summary>
public static class PlayFabClientFactory
{
    /// <summary>Applies TitleId and SecretKey to PlayFab static settings.</summary>
    public static void Configure(CliConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.TitleId))
            throw new InvalidOperationException(
                "TitleId is required. Set it via --title-id, PLAYFAB_TITLE_ID env var, or 'playfab config set'.");

        if (string.IsNullOrWhiteSpace(config.SecretKey))
            throw new InvalidOperationException(
                "SecretKey is required. Set it via --secret-key, PLAYFAB_SECRET_KEY env var, or 'playfab config set'.");

        PlayFabSettings.staticSettings.TitleId = config.TitleId;
        PlayFabSettings.staticSettings.DeveloperSecretKey = config.SecretKey;
    }

    /// <summary>Configures SDK and obtains an entity token for Progression API calls.</summary>
    public static async Task AuthenticateEntityAsync(CliConfig config)
    {
        Configure(config);

        var result = await PlayFabAuthenticationAPI.GetEntityTokenAsync(new GetEntityTokenRequest());
        if (result.Error is not null)
            throw new InvalidOperationException($"Entity authentication failed: {result.Error.ErrorMessage}");
    }
}
