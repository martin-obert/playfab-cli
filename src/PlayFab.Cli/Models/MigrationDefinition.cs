namespace PlayFab.Cli.Models;

public sealed class MigrationDefinition
{
    public required string Description { get; init; }
    public required List<MigrationStep> Up { get; init; }
    public required List<MigrationStep> Down { get; init; }
}

public sealed class MigrationStep
{
    public required string Type { get; init; }
    public string? Key { get; init; }
    public string? Value { get; init; }
    public List<string>? Keys { get; init; }
    public Dictionary<string, string>? Values { get; init; }

    // Statistic-definition fields (create-statistic / delete-statistic)
    public string? Name { get; init; }
    public string? EntityType { get; init; }
    public string? AggregationMethod { get; init; }
    public string? ResetInterval { get; init; }
    public int? MaxQueryableVersions { get; init; }
    public List<StatisticColumnStep>? Columns { get; init; }
}

/// <summary>A single column within a multi-column statistic definition.</summary>
public sealed class StatisticColumnStep
{
    public required string Name { get; init; }
    public required string AggregationMethod { get; init; }
}
