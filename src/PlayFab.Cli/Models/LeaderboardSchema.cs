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

            if (stat.Columns.Count == 0 && !Enum.IsDefined(stat.AggregationMethod))
                errors.Add($"'{stat.Name}': invalid aggregationMethod '{stat.AggregationMethod}'.");

            if (stat.Columns.Count > 0)
            {
                var colNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var col in stat.Columns)
                {
                    if (string.IsNullOrWhiteSpace(col.Name))
                        errors.Add($"'{stat.Name}': a column has an empty name.");
                    else if (!colNames.Add(col.Name))
                        errors.Add($"'{stat.Name}': duplicate column name '{col.Name}'.");

                    if (!Enum.IsDefined(col.AggregationMethod))
                        errors.Add($"'{stat.Name}'.'{col.Name}': invalid aggregationMethod.");
                }
            }

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

    /// <summary>
    /// Shorthand for a single-column statistic. Ignored if Columns is set.
    /// </summary>
    [JsonPropertyName("aggregationMethod")]
    public AggregationMethod AggregationMethod { get; set; } = AggregationMethod.Last;

    [JsonPropertyName("columns")]
    public List<ColumnConfig> Columns { get; set; } = [];

    [JsonPropertyName("resetInterval")]
    public ResetInterval ResetInterval { get; set; } = ResetInterval.Manual;

    [JsonPropertyName("maxQueryableVersions")]
    public int MaxQueryableVersions { get; set; } = 1;

    public List<ColumnConfig> GetEffectiveColumns()
    {
        if (Columns.Count > 0)
            return Columns;

        return [new ColumnConfig { Name = "Value", AggregationMethod = AggregationMethod }];
    }
}

public sealed class ColumnConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("aggregationMethod")]
    public AggregationMethod AggregationMethod { get; set; } = AggregationMethod.Last;
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
    Manual,
    Hour,
    Day,
    Week,
    Month,
}
