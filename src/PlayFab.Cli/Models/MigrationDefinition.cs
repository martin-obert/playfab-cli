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

    // Statistic/leaderboard-definition fields (create-statistic/delete-statistic, create-leaderboard/delete-leaderboard)
    public string? Name { get; init; }
    public string? EntityType { get; init; }
    public string? ResetInterval { get; init; }
    public int? MaxQueryableVersions { get; init; }

    // Statistic-definition fields (create-statistic / delete-statistic)
    public string? AggregationMethod { get; init; }
    public List<StatisticColumnStep>? Columns { get; init; }

    // Leaderboard-definition fields (create-leaderboard / delete-leaderboard)
    public int? SizeLimit { get; init; }
    public List<LeaderboardColumnStep>? LeaderboardColumns { get; init; }
}

/// <summary>A single column within a multi-column statistic definition.</summary>
public sealed class StatisticColumnStep
{
    public required string Name { get; init; }
    public required string AggregationMethod { get; init; }
}

/// <summary>A single column within a leaderboard definition.</summary>
public sealed class LeaderboardColumnStep
{
    public required string Name { get; init; }
    public required string SortDirection { get; init; }

    // Optional link to an existing statistic's column (must both be set or both be omitted)
    public string? LinkedStatistic { get; init; }
    public string? LinkedStatisticColumn { get; init; }
}
