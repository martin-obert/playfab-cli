using System.Text.Json;

namespace PlayFab.Cli.Infrastructure;

/// <summary>Persisted CLI configuration at ~/.playfab/config.json.</summary>
public sealed class CliConfig
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".playfab");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    public string? TitleId { get; set; }
    public string? SecretKey { get; set; }

    /// <summary>Loads config from disk, or returns empty if no file exists.</summary>
    public static CliConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new CliConfig();

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<CliConfig>(json, JsonOptions) ?? new CliConfig();
    }

    /// <summary>Persists current config to disk.</summary>
    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    /// <summary>Resolves credentials: CLI args beat env vars beat config file.</summary>
    public CliConfig MergeWith(string? titleId, string? secretKey)
    {
        return new CliConfig
        {
            TitleId = titleId
                      ?? Environment.GetEnvironmentVariable("PLAYFAB_TITLE_ID")
                      ?? TitleId,
            SecretKey = secretKey
                            ?? Environment.GetEnvironmentVariable("PLAYFAB_SECRET_KEY")
                        ?? SecretKey,
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
