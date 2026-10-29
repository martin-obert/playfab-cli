using System.Text.Json;
using Bogus;
using PlayFab;
using PlayFab.Cli.Models;
using AdminModels = PlayFab.AdminModels;
using ServerModels = PlayFab.ServerModels;

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
        var customId = $"synth-{Guid.NewGuid():N}";

        var loginResult = await PlayFabServerAPI.LoginWithServerCustomIdAsync(
            new ServerModels.LoginWithServerCustomIdRequest
            {
                ServerCustomId = customId,
                CreateAccount = true,
            });

        if (loginResult.Error is not null)
            throw new InvalidOperationException(
                $"LoginWithServerCustomId failed: {loginResult.Error.ErrorMessage}");

        var playFabId = loginResult.Result.PlayFabId;

        await SetDisplayNameWithRetryAsync(playFabId, displayName);

        return new SyntheticPlayer
        {
            PlayFabId = playFabId,
            CustomId = customId,
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

    private static async Task SetDisplayNameWithRetryAsync(string playFabId, string displayName)
    {
        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var name = attempt == 0 ? displayName : GenerateDisplayName();
            var result = await PlayFabAdminAPI.UpdateUserTitleDisplayNameAsync(
                new AdminModels.UpdateUserTitleDisplayNameRequest
                {
                    PlayFabId = playFabId,
                    DisplayName = name,
                });

            if (result.Error is null)
                return;

            if (attempt == maxAttempts - 1)
                throw new InvalidOperationException(
                    $"UpdateUserTitleDisplayName failed after {maxAttempts} attempts: {result.Error.ErrorMessage}");
        }
    }
}
