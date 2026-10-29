using System.Text.Json;
using Bogus;
using PlayFab;
using PlayFab.Cli.Models;
using AdminModels = PlayFab.AdminModels;
using ServerModels = PlayFab.ServerModels;
using ProfilesModels = PlayFab.ProfilesModels;

namespace PlayFab.Cli.Infrastructure;

public static class SyntheticPlayerService
{
    private static readonly string DefaultConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".playfab");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly Faker Faker = new();

    public static string GetFilePath(string titleId, string? configDirectory = null)
    {
        var dir = configDirectory ?? DefaultConfigDir;
        return Path.Combine(dir, $"synthetic-players-{titleId.ToUpperInvariant()}.json");
    }

    public static SyntheticPlayerStore Load(string titleId, string? configDirectory = null)
    {
        var path = GetFilePath(titleId, configDirectory);
        if (!File.Exists(path))
            return new SyntheticPlayerStore { TitleId = titleId };

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SyntheticPlayerStore>(json, JsonOptions)
            ?? new SyntheticPlayerStore { TitleId = titleId };
    }

    public static void Save(SyntheticPlayerStore store, string? configDirectory = null)
    {
        var path = GetFilePath(store.TitleId, configDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(store, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static string GenerateDisplayName()
    {
        var name = Faker.Internet.UserName();
        return name.Length > 25 ? name[..25] : name;
    }

    public static async Task<SyntheticPlayer> CreateAccountAsync(string displayName)
    {
        var serverCustomId = $"synth-{Guid.NewGuid():N}";

        var loginResult = await PlayFabServerAPI.LoginWithServerCustomIdAsync(
            new ServerModels.LoginWithServerCustomIdRequest
            {
                ServerCustomId = serverCustomId,
                CreateAccount = true,
            });

        if (loginResult.Error is not null)
            throw new InvalidOperationException(
                $"LoginWithServerCustomId failed: {loginResult.Error.ErrorMessage}");

        var playFabId = loginResult.Result.PlayFabId;

        var displayResult = await PlayFabAdminAPI.UpdateUserTitleDisplayNameAsync(
            new AdminModels.UpdateUserTitleDisplayNameRequest
            {
                PlayFabId = playFabId,
                DisplayName = displayName,
            });

        if (displayResult.Error is not null)
            throw new InvalidOperationException(
                $"UpdateUserTitleDisplayName failed: {displayResult.Error.ErrorMessage}");

        // Classic UpdateUserTitleDisplayName only sets the legacy display name. Progression/Leaderboard APIs
        // read the Entity Profile's display name instead, which requires this separate call to populate.
        var profileResult = await PlayFabProfilesAPI.SetDisplayNameAsync(
            new ProfilesModels.SetDisplayNameRequest
            {
                Entity = new ProfilesModels.EntityKey { Id = playFabId, Type = "title_player_account" },
                DisplayName = displayName,
            });

        if (profileResult.Error is not null)
            throw new InvalidOperationException(
                $"SetDisplayName failed: {profileResult.Error.ErrorMessage}");

        return new SyntheticPlayer
        {
            PlayFabId = playFabId,
            ServerCustomId = serverCustomId,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        };
    }

    public static async Task DeleteAccountAsync(string playFabId)
    {
        var result = await PlayFabAdminAPI.DeletePlayerAsync(
            new AdminModels.DeletePlayerRequest { PlayFabId = playFabId });

        if (result.Error is not null)
            throw new InvalidOperationException(
                $"DeletePlayer failed for '{playFabId}': {result.Error.ErrorMessage}");
    }

}
