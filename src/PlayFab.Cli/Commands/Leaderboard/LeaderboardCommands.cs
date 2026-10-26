using System.CommandLine;
using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;
using PlayFab.ProgressionModels;
using PlayFab;
using AggregationMethod = PlayFab.Cli.Models.AggregationMethod;
using ResetInterval = PlayFab.Cli.Models.ResetInterval;
using SortDirection = PlayFab.Cli.Models.SortDirection;

namespace PlayFab.Cli.Commands.Leaderboard;

public static class LeaderboardCommands
{
    public static Command CreateGroup(Option<string?> globalTitleId, Option<string?> globalSecretKey, Option<OutputFormat> outputOption)
    {
        var group = new Command("leaderboard", "Manage PlayFab statistics and leaderboards from a local schema file");
        group.Add(CreateListCommand(globalTitleId, globalSecretKey, outputOption));
        group.Add(CreateValidateCommand());
        group.Add(CreateSyncCommand(globalTitleId, globalSecretKey, outputOption));
        return group;
    }

    // ── list ──────────────────────────────────────────────────────────────

    private static Command CreateListCommand(Option<string?> globalTitleId, Option<string?> globalSecretKey, Option<OutputFormat> outputOption)
    {
        var command = new Command("list", "List all statistic and leaderboard definitions from PlayFab");

        command.SetAction(async parseResult =>
        {
            var config = CliConfig.Load().MergeWith(
                parseResult.GetValue(globalTitleId),
                parseResult.GetValue(globalSecretKey));
            await PlayFabClientFactory.AuthenticateEntityAsync(config);

            var output = parseResult.GetValue(outputOption);
            var stats = await FetchAllStatisticDefinitions();
            var leaderboards = await FetchAllLeaderboardDefinitions();

            if (output == OutputFormat.Table)
            {
                if (stats.Count > 0)
                {
                    var rows = new Dictionary<string, string>();
                    foreach (var s in stats)
                    {
                        var cols = string.Join(", ", (s.Columns ?? []).Select(c => $"{c.Name}({c.AggregationMethod})"));
                        var reset = s.VersionConfiguration?.ResetInterval.ToString() ?? "Manual";
                        rows[s.Name] = $"[{cols}] reset: {reset} v{s.Version}";
                    }
                    OutputHelper.WriteTable($"Statistics ({stats.Count})", rows);
                }

                if (leaderboards.Count > 0)
                {
                    var rows = new Dictionary<string, string>();
                    foreach (var lb in leaderboards)
                    {
                        var cols = string.Join(", ", (lb.Columns ?? []).Select(c =>
                        {
                            var linked = c.LinkedStatisticColumn is not null
                                ? $"→{c.LinkedStatisticColumn.LinkedStatisticName}.{c.LinkedStatisticColumn.LinkedStatisticColumnName}"
                                : "";
                            return $"{c.Name}({c.SortDirection}){linked}";
                        }));
                        var reset = lb.VersionConfiguration?.ResetInterval.ToString() ?? "Manual";
                        rows[lb.Name] = $"[{cols}] size: {lb.SizeLimit} reset: {reset} v{lb.Version}";
                    }
                    OutputHelper.WriteTable($"Leaderboards ({leaderboards.Count})", rows);
                }
            }
            else
            {
                OutputHelper.WriteJson(new
                {
                    statistics = stats.Select(s => new
                    {
                        name = s.Name,
                        columns = (s.Columns ?? []).Select(c => new { name = c.Name, aggregationMethod = c.AggregationMethod.ToString() }),
                        resetInterval = s.VersionConfiguration?.ResetInterval.ToString() ?? "Manual",
                        version = s.Version,
                    }),
                    leaderboards = leaderboards.Select(lb => new
                    {
                        name = lb.Name,
                        columns = (lb.Columns ?? []).Select(c => new
                        {
                            name = c.Name,
                            sortDirection = c.SortDirection.ToString(),
                            linkedStatistic = c.LinkedStatisticColumn?.LinkedStatisticName,
                            linkedStatisticColumn = c.LinkedStatisticColumn?.LinkedStatisticColumnName,
                        }),
                        sizeLimit = lb.SizeLimit,
                        resetInterval = lb.VersionConfiguration?.ResetInterval.ToString() ?? "Manual",
                        version = lb.Version,
                    }),
                });
            }
        });

        return command;
    }

    // ── validate ──────────────────────────────────────────────────────────

    private static Command CreateValidateCommand()
    {
        var fileOption = new Option<string>("--file") { Description = "Path to schema JSON file", DefaultValueFactory = _ => "leaderboards.json" };

        var command = new Command("validate", "Validate a local schema file");
        command.Add(fileOption);

        command.SetAction(parseResult =>
        {
            var filePath = parseResult.GetRequiredValue(fileOption);

            LeaderboardSchema schema;
            try
            {
                schema = LeaderboardSchema.LoadFromFile(filePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load schema: {ex.Message}");
                Environment.ExitCode = 1;
                return;
            }

            var errors = schema.Validate();
            if (errors.Count > 0)
            {
                Console.Error.WriteLine("Validation errors:");
                foreach (var error in errors)
                    Console.Error.WriteLine($"  - {error}");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine($"Valid: {schema.Statistics.Count} statistic(s), {schema.Leaderboards.Count} leaderboard(s) in {filePath}");
        });

        return command;
    }

    // ── sync ──────────────────────────────────────────────────────────────

    private static Command CreateSyncCommand(Option<string?> globalTitleId, Option<string?> globalSecretKey, Option<OutputFormat> outputOption)
    {
        var fileOption = new Option<string>("--file") { Description = "Path to schema JSON file", DefaultValueFactory = _ => "leaderboards.json" };
        var nameOption = new Option<string?>("--name") { Description = "Sync only the named statistic or leaderboard" };
        var applyOption = new Option<bool>("--apply") { Description = "Apply changes (default is dry-run)", DefaultValueFactory = _ => false };

        var command = new Command("sync", "Compare local schema with PlayFab and optionally apply changes");
        command.Add(fileOption);
        command.Add(nameOption);
        command.Add(applyOption);

        command.SetAction(async parseResult =>
        {
            var filePath = parseResult.GetRequiredValue(fileOption);
            var nameFilter = parseResult.GetValue(nameOption);
            var apply = parseResult.GetValue(applyOption);
            var output = parseResult.GetValue(outputOption);

            LeaderboardSchema schema;
            try
            {
                schema = LeaderboardSchema.LoadFromFile(filePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load schema: {ex.Message}");
                Environment.ExitCode = 1;
                return;
            }

            var errors = schema.Validate();
            if (errors.Count > 0)
            {
                Console.Error.WriteLine("Schema validation errors:");
                foreach (var error in errors)
                    Console.Error.WriteLine($"  - {error}");
                Environment.ExitCode = 1;
                return;
            }

            var config = CliConfig.Load().MergeWith(
                parseResult.GetValue(globalTitleId),
                parseResult.GetValue(globalSecretKey));
            await PlayFabClientFactory.AuthenticateEntityAsync(config);

            var remoteStats = await FetchAllStatisticDefinitions();
            var remoteStatLookup = remoteStats.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

            var remoteLeaderboards = await FetchAllLeaderboardDefinitions();
            var remoteLbLookup = remoteLeaderboards.ToDictionary(lb => lb.Name, StringComparer.OrdinalIgnoreCase);

            var localStats = FilterByName(schema.Statistics, s => s.Name, nameFilter);
            var localLbs = FilterByName(schema.Leaderboards, lb => lb.Name, nameFilter);

            if (nameFilter is not null && localStats.Count == 0 && localLbs.Count == 0)
            {
                Console.Error.WriteLine($"'{nameFilter}' not found in schema file.");
                Environment.ExitCode = 1;
                return;
            }

            var statPlan = BuildStatisticSyncPlan(localStats, remoteStatLookup);
            var lbPlan = BuildLeaderboardSyncPlan(localLbs, remoteLbLookup);

            PrintSyncPlan(statPlan, lbPlan, output);

            var hasChanges = statPlan.Creates.Count + statPlan.Updates.Count + lbPlan.Creates.Count + lbPlan.Updates.Count > 0;

            if (!apply)
            {
                if (hasChanges)
                    Console.WriteLine("\nRun with --apply to push changes.");
                return;
            }

            await ApplyStatisticPlan(statPlan);
            await ApplyLeaderboardPlan(lbPlan);
        });

        return command;
    }

    // ── fetch helpers ─────────────────────────────────────────────────────

    private static async Task<List<StatisticDefinition>> FetchAllStatisticDefinitions()
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

    private static async Task<List<LeaderboardDefinition>> FetchAllLeaderboardDefinitions()
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

    // ── sync plan builders ────────────────────────────────────────────────

    private static List<T> FilterByName<T>(List<T> items, Func<T, string> getName, string? filter)
    {
        if (filter is null) return items;
        return items.Where(i => getName(i).Equals(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static SyncPlan<StatisticConfig, StatisticDefinition> BuildStatisticSyncPlan(
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

    private static SyncPlan<LeaderboardConfig, LeaderboardDefinition> BuildLeaderboardSyncPlan(
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

    private static void PrintSyncPlan(
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

    private static async Task ApplyStatisticPlan(SyncPlan<StatisticConfig, StatisticDefinition> plan)
    {
        foreach (var stat in plan.Creates)
        {
            var columns = stat.GetEffectiveColumns().Select(c => new StatisticColumn
            {
                Name = c.Name,
                AggregationMethod = ToSdkStatAggregation(c.AggregationMethod),
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
                        ResetInterval = ToSdkResetInterval(stat.ResetInterval),
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
                        ResetInterval = ToSdkResetInterval(local.ResetInterval),
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

    private static async Task ApplyLeaderboardPlan(SyncPlan<LeaderboardConfig, LeaderboardDefinition> plan)
    {
        foreach (var lb in plan.Creates)
        {
            var columns = lb.Columns.Select(c =>
            {
                var col = new LeaderboardColumn
                {
                    Name = c.Name,
                    SortDirection = ToSdkSortDirection(c.SortDirection),
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
                        ResetInterval = ToSdkResetInterval(lb.ResetInterval),
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
                        ResetInterval = ToSdkResetInterval(local.ResetInterval),
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

    // ── enum mappers ──────────────────────────────────────────────────────

    private static PlayFab.ProgressionModels.StatisticAggregationMethod ToSdkStatAggregation(AggregationMethod m) => m switch
    {
        AggregationMethod.Min => ProgressionModels.StatisticAggregationMethod.Min,
        AggregationMethod.Max => ProgressionModels.StatisticAggregationMethod.Max,
        AggregationMethod.Sum => ProgressionModels.StatisticAggregationMethod.Sum,
        _ => ProgressionModels.StatisticAggregationMethod.Last,
    };

    private static PlayFab.ProgressionModels.ResetInterval ToSdkResetInterval(ResetInterval r) => r switch
    {
        ResetInterval.Hour => ProgressionModels.ResetInterval.Hour,
        ResetInterval.Day => ProgressionModels.ResetInterval.Day,
        ResetInterval.Week => ProgressionModels.ResetInterval.Week,
        ResetInterval.Month => ProgressionModels.ResetInterval.Month,
        _ => ProgressionModels.ResetInterval.Manual,
    };

    private static LeaderboardSortDirection ToSdkSortDirection(SortDirection d) => d switch
    {
        SortDirection.Ascending => LeaderboardSortDirection.Ascending,
        _ => LeaderboardSortDirection.Descending,
    };

    // ── sync plan type ────────────────────────────────────────────────────

    private sealed class SyncPlan<TLocal, TRemote>
    {
        public List<TLocal> Creates { get; } = [];
        public List<(TLocal Local, TRemote Remote, List<string> Changes)> Updates { get; } = [];
        public List<string> Unchanged { get; } = [];
    }
}
