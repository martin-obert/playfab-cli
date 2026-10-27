using System.Text.Json;

namespace PlayFab.Cli.Infrastructure;

/// <summary>A synthetic PlayFab account created for leaderboard seeding.</summary>
public sealed class SyntheticAccount
{
    public string PlayFabId { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string ServerCustomId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

/// <summary>Persists synthetic accounts at ~/.playfab/synthetic-accounts/{titleId}.json, scoped per title.</summary>
public static class SyntheticAccountStore
{
    private static readonly string StoreDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".playfab", "synthetic-accounts");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string PathFor(string titleId) => Path.Combine(StoreDir, $"{titleId}.json");

    /// <summary>Loads stored accounts for a title, or an empty list if none exist yet.</summary>
    public static List<SyntheticAccount> Load(string titleId)
    {
        var path = PathFor(titleId);
        if (!File.Exists(path))
            return [];

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<SyntheticAccount>>(json, JsonOptions) ?? [];
    }

    /// <summary>Persists the account list for a title.</summary>
    public static void Save(string titleId, List<SyntheticAccount> accounts)
    {
        Directory.CreateDirectory(StoreDir);
        var json = JsonSerializer.Serialize(accounts, JsonOptions);
        File.WriteAllText(PathFor(titleId), json);
    }
}
