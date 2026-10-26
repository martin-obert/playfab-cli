using PlayFab;
using PlayFab.AuthenticationModels;

namespace PlayFab.Cli.Infrastructure;

public static class PlayFabClientFactory
{
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

    public static async Task AuthenticateEntityAsync(CliConfig config)
    {
        Configure(config);

        var result = await PlayFabAuthenticationAPI.GetEntityTokenAsync(new GetEntityTokenRequest());
        if (result.Error is not null)
            throw new InvalidOperationException($"Entity authentication failed: {result.Error.ErrorMessage}");
    }
}
