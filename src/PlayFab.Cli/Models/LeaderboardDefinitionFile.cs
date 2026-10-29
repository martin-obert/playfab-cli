namespace PlayFab.Cli.Models;

public sealed class LeaderboardDefinitionFile
{
    public required string Name { get; init; }
    public string EntityType { get; init; } = "title_player_account";
    public string ResetInterval { get; init; } = "Manual";
    public int MaxQueryableVersions { get; init; } = 1;
    public int SizeLimit { get; init; } = 1000;
    public required List<LeaderboardColumnDefinition> Columns { get; init; }
}

public sealed class LeaderboardColumnDefinition
{
    public required string Name { get; init; }
    public required string SortDirection { get; init; }
    public string? LinkedStatistic { get; init; }
    public string? LinkedStatisticColumn { get; init; }
}
