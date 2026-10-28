using System.Text.Json;
using System.Text.Json.Nodes;

namespace PlayFab.Cli.Models;

public sealed class TitleConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public JsonNode? TitleData { get; set; }
    public JsonNode? TitleInternalData { get; set; }
    public JsonNode? Statistics { get; set; }
    public JsonNode? Leaderboards { get; set; }
    public JsonNode? Policies { get; set; }

    public static TitleConfiguration LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TitleConfiguration>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize configuration from '{path}'.");
    }

    public List<string> Validate()
    {
        var errors = new List<string>();

        if (TitleData is null && TitleInternalData is null && Statistics is null
            && Leaderboards is null && Policies is null)
        {
            errors.Add("Configuration must contain at least one section.");
            return errors;
        }

        ValidateTitleDataKeys(TitleData, "titleData", errors);
        ValidateTitleDataKeys(TitleInternalData, "titleInternalData", errors);

        return errors;
    }

    private static void ValidateTitleDataKeys(JsonNode? node, string sectionName, List<string> errors)
    {
        if (node is not JsonObject obj)
            return;

        foreach (var (key, _) in obj)
        {
            if (string.IsNullOrWhiteSpace(key))
                errors.Add($"{sectionName}: keys must not be empty.");
        }
    }
}
