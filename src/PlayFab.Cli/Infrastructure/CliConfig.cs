using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayFab.Cli.Infrastructure;

public sealed class CliConfig
{
    private static readonly string DefaultConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".playfab");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string? TitleId { get; set; }
    public string? SecretKey { get; set; }

    [JsonIgnore]
    public string ConfigDirectory { get; private set; } = DefaultConfigDir;

    private string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static CliConfig Load(string? configDirectory = null)
    {
        var dir = configDirectory ?? DefaultConfigDir;
        var path = Path.Combine(dir, "config.json");

        CliConfig config;
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            config = JsonSerializer.Deserialize<CliConfig>(json, JsonOptions) ?? new CliConfig();
        }
        else
        {
            config = new CliConfig();
        }

        config.ConfigDirectory = dir;
        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDirectory);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
