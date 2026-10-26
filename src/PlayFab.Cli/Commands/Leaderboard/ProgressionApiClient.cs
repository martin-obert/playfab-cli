using PlayFab;
using PlayFab.ProgressionModels;
using PlayFab.Cli.Models;
using AggregationMethod = PlayFab.Cli.Models.AggregationMethod;
using ResetInterval = PlayFab.Cli.Models.ResetInterval;
using SortDirection = PlayFab.Cli.Models.SortDirection;

namespace PlayFab.Cli.Commands.Leaderboard;

/// <summary>Paginated wrappers around PlayFab Progression API.</summary>
internal static class ProgressionApiClient
{
    /// <summary>Fetches all statistic definitions, handling pagination.</summary>
    public static async Task<List<StatisticDefinition>> FetchAllStatisticDefinitions()
    {
        var all = new List<StatisticDefinition>();
        string? skipToken = null;

        do
        {
            var result = await PlayFabProgressionAPI.ListStatisticDefinitionsAsync(
                new ListStatisticDefinitionsRequest { SkipToken = skipToken });

            if (result.Error is not null)
            {
                Console.Error.WriteLine($"Error listing statistics: {result.Error.ErrorMessage}");
                Environment.ExitCode = 1;
                return all;
            }

            all.AddRange(result.Result.StatisticDefinitions ?? []);
            skipToken = result.Result.SkipToken;
        } while (!string.IsNullOrEmpty(skipToken));

        return all;
    }

    /// <summary>Fetches all leaderboard definitions, handling pagination.</summary>
    public static async Task<List<LeaderboardDefinition>> FetchAllLeaderboardDefinitions()
    {
        var all = new List<LeaderboardDefinition>();
        string? skipToken = null;

        do
        {
            var result = await PlayFabProgressionAPI.ListLeaderboardDefinitionsAsync(
                new ListLeaderboardDefinitionsRequest { SkipToken = skipToken });

            if (result.Error is not null)
            {
                Console.Error.WriteLine($"Error listing leaderboards: {result.Error.ErrorMessage}");
                Environment.ExitCode = 1;
                return all;
            }

            all.AddRange(result.Result.LeaderboardDefinitions ?? []);
            skipToken = result.Result.SkipToken;
        } while (!string.IsNullOrEmpty(skipToken));

        return all;
    }

    /// <summary>Maps local enum to SDK StatisticAggregationMethod.</summary>
    public static PlayFab.ProgressionModels.StatisticAggregationMethod ToSdkStatAggregation(AggregationMethod m) => m switch
    {
        AggregationMethod.Min => ProgressionModels.StatisticAggregationMethod.Min,
        AggregationMethod.Max => ProgressionModels.StatisticAggregationMethod.Max,
        AggregationMethod.Sum => ProgressionModels.StatisticAggregationMethod.Sum,
        _ => ProgressionModels.StatisticAggregationMethod.Last,
    };

    /// <summary>Maps local enum to SDK ResetInterval.</summary>
    public static PlayFab.ProgressionModels.ResetInterval ToSdkResetInterval(ResetInterval r) => r switch
    {
        ResetInterval.Hour => ProgressionModels.ResetInterval.Hour,
        ResetInterval.Day => ProgressionModels.ResetInterval.Day,
        ResetInterval.Week => ProgressionModels.ResetInterval.Week,
        ResetInterval.Month => ProgressionModels.ResetInterval.Month,
        _ => ProgressionModels.ResetInterval.Manual,
    };

    /// <summary>Maps local enum to SDK LeaderboardSortDirection.</summary>
    public static LeaderboardSortDirection ToSdkSortDirection(SortDirection d) => d switch
    {
        SortDirection.Ascending => LeaderboardSortDirection.Ascending,
        _ => LeaderboardSortDirection.Descending,
    };
}
