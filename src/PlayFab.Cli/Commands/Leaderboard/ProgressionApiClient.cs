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

    /// <summary>Increments a statistic's version, freezing the current version and starting a fresh one. Also resets any leaderboards linked to it.</summary>
    public static async Task<uint?> IncrementStatisticVersion(string name)
    {
        var result = await PlayFabProgressionAPI.IncrementStatisticVersionAsync(new IncrementStatisticVersionRequest { Name = name });

        if (result.Error is not null)
        {
            Console.Error.WriteLine($"Failed to reset statistic '{name}': {result.Error.ErrorMessage}");
            Environment.ExitCode = 1;
            return null;
        }

        return result.Result.Version;
    }

    /// <summary>Increments a standalone leaderboard's version, freezing the current version and starting a fresh one. Fails if the leaderboard is linked to a statistic.</summary>
    public static async Task<uint?> IncrementLeaderboardVersion(string name)
    {
        var result = await PlayFabProgressionAPI.IncrementLeaderboardVersionAsync(new IncrementLeaderboardVersionRequest { Name = name });

        if (result.Error is not null)
        {
            Console.Error.WriteLine($"Failed to reset leaderboard '{name}': {result.Error.ErrorMessage}");
            Environment.ExitCode = 1;
            return null;
        }

        return result.Result.Version;
    }

    /// <summary>Pushes a single statistic value for one entity. Also feeds any leaderboard linked to this statistic.</summary>
    public static async Task<bool> UpdateStatisticValue(EntityKey entity, string statisticName, List<string> scores)
    {
        var result = await PlayFabProgressionAPI.UpdateStatisticsAsync(new UpdateStatisticsRequest
        {
            Entity = entity,
            Statistics = [new StatisticUpdate { Name = statisticName, Scores = scores }],
        });

        if (result.Error is not null)
        {
            Console.Error.WriteLine($"Failed to update statistic '{statisticName}' for entity '{entity.Id}': {result.Error.ErrorMessage}");
            Environment.ExitCode = 1;
            return false;
        }

        return true;
    }

    /// <summary>Pushes a batch of entries directly into a standalone (non-linked) leaderboard.</summary>
    public static async Task<bool> UpdateLeaderboardEntriesBatch(string leaderboardName, List<LeaderboardEntryUpdate> entries)
    {
        var result = await PlayFabProgressionAPI.UpdateLeaderboardEntriesAsync(new UpdateLeaderboardEntriesRequest
        {
            LeaderboardName = leaderboardName,
            Entries = entries,
        });

        if (result.Error is not null)
        {
            Console.Error.WriteLine($"Failed to update {entries.Count} entries for leaderboard '{leaderboardName}': {result.Error.ErrorMessage}");
            Environment.ExitCode = 1;
            return false;
        }

        return true;
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
