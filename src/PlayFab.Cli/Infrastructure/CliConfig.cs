using System.Text.Json;

namespace PlayFab.Cli.Infrastructure;

public sealed class CliConfig
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".playfab");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    public string? TitleId { get; set; }
    public string? SecretKey { get; set; }

    public static CliConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new CliConfig();

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<CliConfig>(json, JsonOptions) ?? new CliConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

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
