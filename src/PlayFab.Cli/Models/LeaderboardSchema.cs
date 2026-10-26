using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayFab.Cli.Models;

public sealed class LeaderboardSchema
{
    [JsonPropertyName("statistics")]
    public List<StatisticConfig> Statistics { get; set; } = [];

    [JsonPropertyName("leaderboards")]
    public List<LeaderboardConfig> Leaderboards { get; set; } = [];

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

        if (Statistics.Count == 0 && Leaderboards.Count == 0)
            errors.Add("Schema contains no statistics or leaderboards.");

        var statNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stat in Statistics)
        {
            if (string.IsNullOrWhiteSpace(stat.Name))
            {
                errors.Add("A statistic has an empty or missing name.");
                continue;
            }

            if (!statNames.Add(stat.Name))
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

        var lbNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lb in Leaderboards)
        {
            if (string.IsNullOrWhiteSpace(lb.Name))
            {
                errors.Add("A leaderboard has an empty or missing name.");
                continue;
            }

            if (!lbNames.Add(lb.Name))
                errors.Add($"Duplicate leaderboard name: '{lb.Name}'.");

            if (lb.Columns.Count == 0)
                errors.Add($"Leaderboard '{lb.Name}': must have at least one column.");

            foreach (var col in lb.Columns)
            {
                if (string.IsNullOrWhiteSpace(col.Name))
                    errors.Add($"Leaderboard '{lb.Name}': a column has an empty name.");

                if (!Enum.IsDefined(col.SortDirection))
                    errors.Add($"Leaderboard '{lb.Name}'.'{col.Name}': invalid sortDirection.");

                if (!string.IsNullOrEmpty(col.LinkedStatistic) && string.IsNullOrEmpty(col.LinkedStatisticColumn))
                    errors.Add($"Leaderboard '{lb.Name}'.'{col.Name}': linkedStatisticColumn is required when linkedStatistic is set.");

                if (!string.IsNullOrEmpty(col.LinkedStatistic) && !statNames.Contains(col.LinkedStatistic))
                    errors.Add($"Leaderboard '{lb.Name}'.'{col.Name}': linkedStatistic '{col.LinkedStatistic}' not found in statistics.");
            }

            if (!Enum.IsDefined(lb.ResetInterval))
                errors.Add($"Leaderboard '{lb.Name}': invalid resetInterval '{lb.ResetInterval}'.");
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

    [JsonPropertyName("columns")]
    public List<StatisticColumnConfig> Columns { get; set; } = [];

    [JsonPropertyName("resetInterval")]
    public ResetInterval ResetInterval { get; set; } = ResetInterval.Manual;

    [JsonPropertyName("maxQueryableVersions")]
    public int MaxQueryableVersions { get; set; } = 1;

    public List<StatisticColumnConfig> GetEffectiveColumns()
    {
        if (Columns.Count > 0)
            return Columns;

        return [new StatisticColumnConfig { Name = "Value", AggregationMethod = AggregationMethod }];
    }
}

public sealed class StatisticColumnConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("aggregationMethod")]
    public AggregationMethod AggregationMethod { get; set; } = AggregationMethod.Last;
}

public sealed class LeaderboardConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("columns")]
    public List<LeaderboardColumnConfig> Columns { get; set; } = [];

    [JsonPropertyName("sizeLimit")]
    public int SizeLimit { get; set; } = 1000;

    [JsonPropertyName("resetInterval")]
    public ResetInterval ResetInterval { get; set; } = ResetInterval.Manual;

    [JsonPropertyName("maxQueryableVersions")]
    public int MaxQueryableVersions { get; set; } = 1;
}

public sealed class LeaderboardColumnConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("sortDirection")]
    public SortDirection SortDirection { get; set; } = SortDirection.Descending;

    [JsonPropertyName("linkedStatistic")]
    public string? LinkedStatistic { get; set; }

    [JsonPropertyName("linkedStatisticColumn")]
    public string? LinkedStatisticColumn { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<AggregationMethod>))]
public enum AggregationMethod { Last, Min, Max, Sum }

[JsonConverter(typeof(JsonStringEnumConverter<ResetInterval>))]
public enum ResetInterval { Manual, Hour, Day, Week, Month }

[JsonConverter(typeof(JsonStringEnumConverter<SortDirection>))]
public enum SortDirection { Descending, Ascending }
