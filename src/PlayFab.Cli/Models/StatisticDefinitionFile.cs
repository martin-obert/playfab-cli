namespace PlayFab.Cli.Models;

public sealed class StatisticDefinitionFile
{
    public required string Name { get; init; }
    public string EntityType { get; init; } = "title_player_account";
    public string ResetInterval { get; init; } = "Manual";
    public int MaxQueryableVersions { get; init; } = 1;
    public required List<StatisticColumnDefinition> Columns { get; init; }
}

public sealed class StatisticColumnDefinition
{
    public required string Name { get; init; }
    public required string AggregationMethod { get; init; }
    public string Type { get; init; } = "string";
}
