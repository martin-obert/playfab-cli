using PlayFab;
using PlayFab.ProgressionModels;
using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;

namespace PlayFab.Cli.Commands.Leaderboard;

/// <summary>Builds, prints, and applies sync plans that compare local schema against remote PlayFab state.</summary>
internal static class SyncEngine
{
    /// <summary>Filters items by name (case-insensitive). Returns all if filter is null.</summary>
    public static List<T> FilterByName<T>(List<T> items, Func<T, string> getName, string? filter)
    {
        if (filter is null) return items;
        return items.Where(i => getName(i).Equals(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Compares local statistic configs against remote definitions.</summary>
    public static SyncPlan<StatisticConfig, StatisticDefinition> BuildStatisticSyncPlan(
        List<StatisticConfig> localStats, Dictionary<string, StatisticDefinition> remoteLookup)
    {
        var plan = new SyncPlan<StatisticConfig, StatisticDefinition>();

        foreach (var local in localStats)
        {
            if (!remoteLookup.TryGetValue(local.Name, out var remote))
            {
                plan.Creates.Add(local);
                continue;
            }

            var changes = new List<string>();

            var remoteReset = remote.VersionConfiguration?.ResetInterval.ToString() ?? "Manual";
            if (!remoteReset.Equals(local.ResetInterval.ToString(), StringComparison.OrdinalIgnoreCase))
                changes.Add($"resetInterval: {remoteReset} → {local.ResetInterval}");

            var remoteMaxVersions = remote.VersionConfiguration?.MaxQueryableVersions ?? 1;
            if (remoteMaxVersions != local.MaxQueryableVersions)
                changes.Add($"maxQueryableVersions: {remoteMaxVersions} → {local.MaxQueryableVersions}");

            var localCols = local.GetEffectiveColumns();
            var remoteCols = remote.Columns ?? [];
            if (localCols.Count != remoteCols.Count)
            {
                changes.Add($"columns: {remoteCols.Count} → {localCols.Count} (cannot update after creation)");
            }
            else
            {
                for (var i = 0; i < localCols.Count; i++)
                {
                    if (!localCols[i].Name.Equals(remoteCols[i].Name, StringComparison.OrdinalIgnoreCase)
                        || !localCols[i].AggregationMethod.ToString().Equals(remoteCols[i].AggregationMethod.ToString(), StringComparison.OrdinalIgnoreCase))
                        changes.Add($"column '{remoteCols[i].Name}' differs (cannot update after creation)");
                }
            }

            if (changes.Count > 0)
                plan.Updates.Add((local, remote, changes));
            else
                plan.Unchanged.Add(local.Name);
        }

        return plan;
    }

    /// <summary>Compares local leaderboard configs against remote definitions.</summary>
    public static SyncPlan<LeaderboardConfig, LeaderboardDefinition> BuildLeaderboardSyncPlan(
        List<LeaderboardConfig> localLbs, Dictionary<string, LeaderboardDefinition> remoteLookup)
    {
        var plan = new SyncPlan<LeaderboardConfig, LeaderboardDefinition>();

        foreach (var local in localLbs)
        {
            if (!remoteLookup.TryGetValue(local.Name, out var remote))
            {
                plan.Creates.Add(local);
                continue;
            }

            var changes = new List<string>();

            var remoteReset = remote.VersionConfiguration?.ResetInterval.ToString() ?? "Manual";
            if (!remoteReset.Equals(local.ResetInterval.ToString(), StringComparison.OrdinalIgnoreCase))
                changes.Add($"resetInterval: {remoteReset} → {local.ResetInterval}");

            if (remote.SizeLimit != local.SizeLimit)
                changes.Add($"sizeLimit: {remote.SizeLimit} → {local.SizeLimit}");

            var remoteCols = remote.Columns ?? [];
            if (local.Columns.Count != remoteCols.Count)
            {
                changes.Add($"columns: {remoteCols.Count} → {local.Columns.Count} (cannot update after creation)");
            }
            else
            {
                for (var i = 0; i < local.Columns.Count; i++)
                {
                    var lc = local.Columns[i];
                    var rc = remoteCols[i];
                    if (!lc.Name.Equals(rc.Name, StringComparison.OrdinalIgnoreCase)
                        || !lc.SortDirection.ToString().Equals(rc.SortDirection.ToString(), StringComparison.OrdinalIgnoreCase))
                        changes.Add($"column '{rc.Name}' differs (cannot update after creation)");
                }
            }

            if (changes.Count > 0)
                plan.Updates.Add((local, remote, changes));
            else
                plan.Unchanged.Add(local.Name);
        }

        return plan;
    }

    // ── output ────────────────────────────────────────────────────────────

    /// <summary>Prints both sync plans in the requested output format.</summary>
    public static void PrintSyncPlan(
        SyncPlan<StatisticConfig, StatisticDefinition> statPlan,
        SyncPlan<LeaderboardConfig, LeaderboardDefinition> lbPlan,
        OutputFormat output)
    {
        if (output == OutputFormat.Json)
        {
            OutputHelper.WriteJson(new
            {
                statistics = new
                {
                    create = statPlan.Creates.Select(c => c.Name),
                    update = statPlan.Updates.Select(u => new { name = u.Local.Name, changes = u.Changes }),
                    unchanged = statPlan.Unchanged,
                },
                leaderboards = new
                {
                    create = lbPlan.Creates.Select(c => c.Name),
                    update = lbPlan.Updates.Select(u => new { name = u.Local.Name, changes = u.Changes }),
                    unchanged = lbPlan.Unchanged,
                },
            });
            return;
        }

        PrintSection("Statistics", statPlan, s =>
        {
            var cols = string.Join(", ", s.GetEffectiveColumns().Select(c => $"{c.Name}({c.AggregationMethod})"));
            return $"[{cols}] reset: {s.ResetInterval}";
        });

        PrintSection("Leaderboards", lbPlan, lb =>
        {
            var cols = string.Join(", ", lb.Columns.Select(c =>
            {
                var link = c.LinkedStatistic is not null ? $"→{c.LinkedStatistic}.{c.LinkedStatisticColumn}" : "";
                return $"{c.Name}({c.SortDirection}){link}";
            }));
            return $"[{cols}] size: {lb.SizeLimit} reset: {lb.ResetInterval}";
        });

        var totalChanges = statPlan.Creates.Count + statPlan.Updates.Count + lbPlan.Creates.Count + lbPlan.Updates.Count;
        if (totalChanges == 0)
            Console.WriteLine("Everything is in sync.");
    }

    private static void PrintSection<TLocal, TRemote>(string label, SyncPlan<TLocal, TRemote> plan, Func<TLocal, string> describe)
    {
        if (plan.Creates.Count == 0 && plan.Updates.Count == 0 && plan.Unchanged.Count == 0)
            return;

        Console.WriteLine($"\n{label}:");

        foreach (var c in plan.Creates)
            Console.WriteLine($"  + {GetName(c)} {describe(c)}");

        foreach (var (local, _, changes) in plan.Updates)
        {
            Console.WriteLine($"  ~ {GetName(local)}");
            foreach (var change in changes)
                Console.WriteLine($"      {change}");
        }

        foreach (var name in plan.Unchanged)
            Console.WriteLine($"  = {name}");
    }

    private static string GetName<T>(T item) => item switch
    {
        StatisticConfig s => s.Name,
        LeaderboardConfig lb => lb.Name,
        _ => item?.ToString() ?? "",
    };

    // ── apply ─────────────────────────────────────────────────────────────

    /// <summary>Creates and updates statistics on PlayFab per the sync plan.</summary>
    public static async Task ApplyStatisticPlan(SyncPlan<StatisticConfig, StatisticDefinition> plan)
    {
        foreach (var stat in plan.Creates)
        {
            var columns = stat.GetEffectiveColumns().Select(c => new StatisticColumn
            {
                Name = c.Name,
                AggregationMethod = ProgressionApiClient.ToSdkStatAggregation(c.AggregationMethod),
            }).ToList();

            var result = await PlayFabProgressionAPI.CreateStatisticDefinitionAsync(
                new CreateStatisticDefinitionRequest
                {
                    Name = stat.Name,
                    Columns = columns,
                    EntityType = "title_player_account",
                    VersionConfiguration = new VersionConfiguration
                    {
                        MaxQueryableVersions = stat.MaxQueryableVersions,
                        ResetInterval = ProgressionApiClient.ToSdkResetInterval(stat.ResetInterval),
                    },
                });

            if (result.Error is not null)
            {
                Console.Error.WriteLine($"Failed to create statistic '{stat.Name}': {result.Error.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine($"Created statistic: {stat.Name}");
        }

        foreach (var (local, _, changes) in plan.Updates)
        {
            if (changes.Any(c => c.Contains("cannot update")))
            {
                Console.Error.WriteLine($"Skipped statistic '{local.Name}': column changes require delete + recreate.");
                continue;
            }

            var result = await PlayFabProgressionAPI.UpdateStatisticDefinitionAsync(
                new UpdateStatisticDefinitionRequest
                {
                    Name = local.Name,
                    VersionConfiguration = new VersionConfiguration
                    {
                        MaxQueryableVersions = local.MaxQueryableVersions,
                        ResetInterval = ProgressionApiClient.ToSdkResetInterval(local.ResetInterval),
                    },
                });

            if (result.Error is not null)
            {
                Console.Error.WriteLine($"Failed to update statistic '{local.Name}': {result.Error.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine($"Updated statistic: {local.Name}");
        }
    }

    /// <summary>Creates and updates leaderboards on PlayFab per the sync plan.</summary>
    public static async Task ApplyLeaderboardPlan(SyncPlan<LeaderboardConfig, LeaderboardDefinition> plan)
    {
        foreach (var lb in plan.Creates)
        {
            var columns = lb.Columns.Select(c =>
            {
                var col = new LeaderboardColumn
                {
                    Name = c.Name,
                    SortDirection = ProgressionApiClient.ToSdkSortDirection(c.SortDirection),
                };

                if (c.LinkedStatistic is not null)
                {
                    col.LinkedStatisticColumn = new LinkedStatisticColumn
                    {
                        LinkedStatisticName = c.LinkedStatistic,
                        LinkedStatisticColumnName = c.LinkedStatisticColumn!,
                    };
                }

                return col;
            }).ToList();

            var result = await PlayFabProgressionAPI.CreateLeaderboardDefinitionAsync(
                new CreateLeaderboardDefinitionRequest
                {
                    Name = lb.Name,
                    Columns = columns,
                    EntityType = "title_player_account",
                    SizeLimit = lb.SizeLimit,
                    VersionConfiguration = new VersionConfiguration
                    {
                        MaxQueryableVersions = lb.MaxQueryableVersions,
                        ResetInterval = ProgressionApiClient.ToSdkResetInterval(lb.ResetInterval),
                    },
                });

            if (result.Error is not null)
            {
                Console.Error.WriteLine($"Failed to create leaderboard '{lb.Name}': {result.Error.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine($"Created leaderboard: {lb.Name}");
        }

        foreach (var (local, _, changes) in plan.Updates)
        {
            if (changes.Any(c => c.Contains("cannot update")))
            {
                Console.Error.WriteLine($"Skipped leaderboard '{local.Name}': column changes require delete + recreate.");
                continue;
            }

            var result = await PlayFabProgressionAPI.UpdateLeaderboardDefinitionAsync(
                new UpdateLeaderboardDefinitionRequest
                {
                    Name = local.Name,
                    SizeLimit = local.SizeLimit,
                    VersionConfiguration = new VersionConfiguration
                    {
                        MaxQueryableVersions = local.MaxQueryableVersions,
                        ResetInterval = ProgressionApiClient.ToSdkResetInterval(local.ResetInterval),
                    },
                });

            if (result.Error is not null)
            {
                Console.Error.WriteLine($"Failed to update leaderboard '{local.Name}': {result.Error.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine($"Updated leaderboard: {local.Name}");
        }
    }
}

/// <summary>Diff between local schema and remote PlayFab state.</summary>
internal sealed class SyncPlan<TLocal, TRemote>
{
    public List<TLocal> Creates { get; } = [];
    public List<(TLocal Local, TRemote Remote, List<string> Changes)> Updates { get; } = [];
    public List<string> Unchanged { get; } = [];
}
