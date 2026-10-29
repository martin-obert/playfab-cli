 using PlayFab.ProgressionModels;

namespace PlayFab.Cli.Infrastructure;

public static class StatisticSeedService
{
    /// <summary>Parses a "Name:min-max" column spec, e.g. "Score:100-2000".</summary>
    public static (string Name, int Min, int Max) ParseColumnSpec(string spec)
    {
        var colonIndex = spec.IndexOf(':');
        if (colonIndex <= 0 || colonIndex == spec.Length - 1)
            throw new FormatException($"Invalid --column spec '{spec}'. Expected format 'Name:min-max'.");

        var name = spec[..colonIndex];
        var range = spec[(colonIndex + 1)..];

        var dashIndex = range.IndexOf('-');
        if (dashIndex <= 0 || dashIndex == range.Length - 1)
            throw new FormatException($"Invalid --column spec '{spec}'. Expected format 'Name:min-max'.");

        if (!int.TryParse(range[..dashIndex], out var min) || !int.TryParse(range[(dashIndex + 1)..], out var max))
            throw new FormatException($"Invalid --column spec '{spec}'. min/max must be integers.");

        if (min > max)
            throw new FormatException($"Invalid --column spec '{spec}'. min ({min}) must be <= max ({max}).");

        return (name, min, max);
    }

    /// <summary>Returns an error per requested column name that isn't among the statistic's defined columns.</summary>
    public static List<string> ValidateColumns(IReadOnlyList<string> definedColumnNames, IReadOnlyList<string> requestedColumnNames)
    {
        var defined = new HashSet<string>(definedColumnNames, StringComparer.OrdinalIgnoreCase);
        return requestedColumnNames
            .Where(name => !defined.Contains(name))
            .Select(name => $"Column '{name}' does not exist on this statistic. Available columns: {string.Join(", ", definedColumnNames)}.")
            .ToList();
    }

    /// <summary>
    /// A seed write resends the current value for every column it isn't targeting (PlayFab's write is positional,
    /// not a sparse update). That's safe for Min/Max/Last columns (resending the same value is a no-op under either
    /// an overwrite or an aggregate-on-write server model), but unsafe for Sum columns if the server aggregates
    /// incoming values into the existing one — resending the existing value would double it. Since that server
    /// behavior can't be confirmed without a live title, untouched Sum columns are refused rather than risked.
    /// </summary>
    public static List<string> ValidateUntouchedSumColumns(IReadOnlyList<StatisticColumn> definedColumns, IReadOnlyList<string> requestedColumnNames)
    {
        var requested = new HashSet<string>(requestedColumnNames, StringComparer.OrdinalIgnoreCase);
        return definedColumns
            .Where(c => c.AggregationMethod == StatisticAggregationMethod.Sum && !requested.Contains(c.Name))
            .Select(c => $"Column '{c.Name}' uses Sum aggregation and would be left untouched by this seed — include it in --column or choose a statistic without this issue.")
            .ToList();
    }

    /// <summary>
    /// Builds the full positional Scores list PlayFab requires: targeted columns get a fresh random value,
    /// every other position keeps its current score (or "0" if the player had no prior value there).
    /// </summary>
    public static List<string> MergeScores(IReadOnlyList<string> columnNames, IReadOnlyList<string> currentScores, Dictionary<string, (int Min, int Max)> columnRanges)
    {
        var merged = new List<string>(columnNames.Count);
        for (var i = 0; i < columnNames.Count; i++)
        {
            if (columnRanges.TryGetValue(columnNames[i], out var range))
                merged.Add(Random.Shared.Next(range.Min, range.Max + 1).ToString());
            else
                merged.Add(i < currentScores.Count ? currentScores[i] : "0");
        }
        return merged;
    }

    public static async Task<List<StatisticColumn>> GetColumnsAsync(string statisticName)
    {
        var result = await PlayFabProgressionAPI.GetStatisticDefinitionAsync(new GetStatisticDefinitionRequest { Name = statisticName });
        if (result.Error is not null)
            throw new InvalidOperationException($"GetStatisticDefinition failed for '{statisticName}': {result.Error.ErrorMessage}");

        return result.Result.Columns;
    }

    public static async Task SeedPlayerAsync(string playFabId, string statisticName, IReadOnlyList<string> columnNames, Dictionary<string, (int Min, int Max)> columnRanges)
    {
        var entity = new EntityKey { Id = playFabId, Type = "title_player_account" };

        var getResult = await PlayFabProgressionAPI.GetStatisticsAsync(new GetStatisticsRequest
        {
            Entity = entity,
            StatisticNames = [statisticName],
        });

        if (getResult.Error is not null)
            throw new InvalidOperationException($"GetStatistics failed for '{playFabId}': {getResult.Error.ErrorMessage}");

        var currentScores = getResult.Result.Statistics is not null && getResult.Result.Statistics.TryGetValue(statisticName, out var existing)
            ? existing.Scores
            : [];

        var mergedScores = MergeScores(columnNames, currentScores, columnRanges);

        var updateResult = await PlayFabProgressionAPI.UpdateStatisticsAsync(new UpdateStatisticsRequest
        {
            Entity = entity,
            Statistics =
            [
                new StatisticUpdate { Name = statisticName, Scores = mergedScores },
            ],
        });

        if (updateResult.Error is not null)
            throw new InvalidOperationException($"UpdateStatistics failed for '{playFabId}': {updateResult.Error.ErrorMessage}");
    }
}
