using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayFab.Cli.Models;

public sealed class LeaderboardSchema
{
    [JsonPropertyName("statistics")]
    public List<StatisticConfig> Statistics { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static LeaderboardSchema LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Schema file not found: {path}");

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LeaderboardSchema>(json, JsonOptions)
               ?? throw new InvalidOperationException("Failed to deserialize schema file.");
    }

    public List<string> Validate()
    {
        var errors = new List<string>();

        if (Statistics.Count == 0)
            errors.Add("Schema contains no statistics.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stat in Statistics)
        {
            if (string.IsNullOrWhiteSpace(stat.Name))
            {
                errors.Add("A statistic has an empty or missing name.");
                continue;
            }

            if (!names.Add(stat.Name))
                errors.Add($"Duplicate statistic name: '{stat.Name}'.");

            if (!Enum.IsDefined(stat.AggregationMethod))
                errors.Add($"'{stat.Name}': invalid aggregationMethod '{stat.AggregationMethod}'.");

            if (!Enum.IsDefined(stat.ResetInterval))
                errors.Add($"'{stat.Name}': invalid resetInterval '{stat.ResetInterval}'.");
        }

        return errors;
    }
}

public sealed class StatisticConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("aggregationMethod")]
    public AggregationMethod AggregationMethod { get; set; } = AggregationMethod.Last;

    [JsonPropertyName("resetInterval")]
    public ResetInterval ResetInterval { get; set; } = ResetInterval.Never;
}

[JsonConverter(typeof(JsonStringEnumConverter<AggregationMethod>))]
public enum AggregationMethod
{
    Last,
    Min,
    Max,
    Sum,
}

[JsonConverter(typeof(JsonStringEnumConverter<ResetInterval>))]
public enum ResetInterval
{
    Never,
    Hour,
    Day,
    Week,
    Month,
}
