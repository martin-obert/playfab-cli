using System.CommandLine;
using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;
using PlayFab.ProgressionModels;
using PlayFab;
using AggregationMethod = PlayFab.Cli.Models.AggregationMethod;
using ResetInterval = PlayFab.Cli.Models.ResetInterval;

namespace PlayFab.Cli.Commands.Leaderboard;

public static class LeaderboardCommands
{
    public static Command CreateGroup(Option<string?> globalTitleId, Option<string?> globalSecretKey, Option<OutputFormat> outputOption)
    {
        var group = new Command("leaderboard", "Manage PlayFab leaderboard statistics from a local schema file");
        group.Add(CreateListCommand(globalTitleId, globalSecretKey, outputOption));
        group.Add(CreateValidateCommand());
        group.Add(CreateSyncCommand(globalTitleId, globalSecretKey, outputOption));
        return group;
    }

    private static Command CreateListCommand(Option<string?> globalTitleId, Option<string?> globalSecretKey, Option<OutputFormat> outputOption)
    {
        var command = new Command("list", "List all statistic definitions from PlayFab");

        command.SetAction(async parseResult =>
        {
            var config = CliConfig.Load().MergeWith(
                parseResult.GetValue(globalTitleId),
                parseResult.GetValue(globalSecretKey));
            await PlayFabClientFactory.AuthenticateEntityAsync(config);

            var stats = await FetchAllStatisticDefinitions();
            var output = parseResult.GetValue(outputOption);

            if (output == OutputFormat.Table)
            {
                var rows = new Dictionary<string, string>();
                foreach (var s in stats)
                {
                    var cols = string.Join(", ", (s.Columns ?? []).Select(c => $"{c.Name}({c.AggregationMethod})"));
                    var reset = s.VersionConfiguration?.ResetInterval.ToString() ?? "Manual";
                    rows[s.Name] = $"columns: [{cols}] | reset: {reset} | v{s.Version}";
                }
                OutputHelper.WriteTable($"Statistics ({stats.Count})", rows);
            }
            else
            {
                OutputHelper.WriteJson(stats.Select(s => new
                {
                    name = s.Name,
                    columns = (s.Columns ?? []).Select(c => new
                    {
                        name = c.Name,
                        aggregationMethod = c.AggregationMethod.ToString(),
                    }),
                    resetInterval = s.VersionConfiguration?.ResetInterval.ToString() ?? "Manual",
                    maxQueryableVersions = s.VersionConfiguration?.MaxQueryableVersions ?? 1,
                    version = s.Version,
                    entityType = s.EntityType,
                }));
            }
        });

        return command;
    }

    private static Command CreateValidateCommand()
    {
        var fileOption = new Option<string>("--file") { Description = "Path to leaderboard schema JSON file", DefaultValueFactory = _ => "leaderboards.json" };

        var command = new Command("validate", "Validate a local leaderboard schema file");
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

            Console.WriteLine($"Valid: {schema.Statistics.Count} statistic(s) defined in {filePath}");
        });

        return command;
    }

    private static Command CreateSyncCommand(Option<string?> globalTitleId, Option<string?> globalSecretKey, Option<OutputFormat> outputOption)
    {
        var fileOption = new Option<string>("--file") { Description = "Path to leaderboard schema JSON file", DefaultValueFactory = _ => "leaderboards.json" };
        var nameOption = new Option<string?>("--name") { Description = "Sync only the named statistic" };
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

            var localStats = nameFilter is not null
                ? schema.Statistics.Where(s => s.Name.Equals(nameFilter, StringComparison.OrdinalIgnoreCase)).ToList()
                : schema.Statistics;

            if (localStats.Count == 0)
            {
                Console.Error.WriteLine($"Statistic '{nameFilter}' not found in schema file.");
                Environment.ExitCode = 1;
                return;
            }

            var config = CliConfig.Load().MergeWith(
                parseResult.GetValue(globalTitleId),
                parseResult.GetValue(globalSecretKey));
            await PlayFabClientFactory.AuthenticateEntityAsync(config);

            var remoteStats = await FetchAllStatisticDefinitions();
            var remoteLookup = remoteStats.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

            var plan = BuildSyncPlan(localStats, remoteLookup);

            PrintPlan(plan, output);

            if (!apply)
            {
                if (plan.Creates.Count > 0 || plan.Updates.Count > 0)
                    Console.WriteLine("\nRun with --apply to push changes.");
                return;
            }

            await ApplyPlan(plan);
        });

        return command;
    }

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
                Console.Error.WriteLine($"Error: {result.Error.ErrorMessage}");
                Environment.ExitCode = 1;
                return all;
            }

            all.AddRange(result.Result.StatisticDefinitions ?? []);
            skipToken = result.Result.SkipToken;
        } while (!string.IsNullOrEmpty(skipToken));

        return all;
    }

    private static SyncPlan BuildSyncPlan(IEnumerable<StatisticConfig> localStats, Dictionary<string, StatisticDefinition> remoteLookup)
    {
        var plan = new SyncPlan();

        foreach (var local in localStats)
        {
            if (!remoteLookup.TryGetValue(local.Name, out var remote))
            {
                plan.Creates.Add(local);
                continue;
            }

            var changes = DetectChanges(local, remote);
            if (changes.Count > 0)
                plan.Updates.Add((local, remote, changes));
            else
                plan.Unchanged.Add(local.Name);
        }

        return plan;
    }

    private static List<string> DetectChanges(StatisticConfig local, StatisticDefinition remote)
    {
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
                var lc = localCols[i];
                var rc = remoteCols[i];
                if (!lc.Name.Equals(rc.Name, StringComparison.OrdinalIgnoreCase)
                    || !lc.AggregationMethod.ToString().Equals(rc.AggregationMethod.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    changes.Add($"column '{rc.Name}' differs (cannot update after creation)");
                }
            }
        }

        return changes;
    }

    private static void PrintPlan(SyncPlan plan, OutputFormat output)
    {
        if (output == OutputFormat.Json)
        {
            OutputHelper.WriteJson(new
            {
                create = plan.Creates.Select(c => new { name = c.Name, columns = c.GetEffectiveColumns().Select(col => $"{col.Name}({col.AggregationMethod})") }),
                update = plan.Updates.Select(u => new { name = u.Local.Name, changes = u.Changes }),
                unchanged = plan.Unchanged,
            });
            return;
        }

        if (plan.Creates.Count > 0)
        {
            Console.WriteLine("CREATE:");
            foreach (var c in plan.Creates)
            {
                var cols = string.Join(", ", c.GetEffectiveColumns().Select(col => $"{col.Name}({col.AggregationMethod})"));
                Console.WriteLine($"  + {c.Name} [{cols}] reset: {c.ResetInterval}");
            }
        }

        if (plan.Updates.Count > 0)
        {
            Console.WriteLine("UPDATE:");
            foreach (var (local, _, changes) in plan.Updates)
            {
                Console.WriteLine($"  ~ {local.Name}");
                foreach (var change in changes)
                    Console.WriteLine($"      {change}");
            }
        }

        if (plan.Unchanged.Count > 0)
        {
            Console.WriteLine("UNCHANGED:");
            foreach (var name in plan.Unchanged)
                Console.WriteLine($"  = {name}");
        }

        if (plan.Creates.Count == 0 && plan.Updates.Count == 0)
            Console.WriteLine("Everything is in sync.");
    }

    private static async Task ApplyPlan(SyncPlan plan)
    {
        foreach (var stat in plan.Creates)
        {
            var columns = stat.GetEffectiveColumns().Select(c => new StatisticColumn
            {
                Name = c.Name,
                AggregationMethod = ToSdkAggregation(c.AggregationMethod),
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
                Console.Error.WriteLine($"Failed to create '{stat.Name}': {result.Error.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine($"Created: {stat.Name}");
        }

        foreach (var (local, _, changes) in plan.Updates)
        {
            var columnChanges = changes.Any(c => c.Contains("cannot update after creation"));
            if (columnChanges)
            {
                Console.Error.WriteLine($"Skipped '{local.Name}': column changes require delete + recreate (not supported in sync).");
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
                Console.Error.WriteLine($"Failed to update '{local.Name}': {result.Error.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine($"Updated: {local.Name}");
        }
    }

    private static PlayFab.ProgressionModels.StatisticAggregationMethod ToSdkAggregation(AggregationMethod method) => method switch
    {
        AggregationMethod.Last => PlayFab.ProgressionModels.StatisticAggregationMethod.Last,
        AggregationMethod.Min => PlayFab.ProgressionModels.StatisticAggregationMethod.Min,
        AggregationMethod.Max => PlayFab.ProgressionModels.StatisticAggregationMethod.Max,
        AggregationMethod.Sum => PlayFab.ProgressionModels.StatisticAggregationMethod.Sum,
        _ => PlayFab.ProgressionModels.StatisticAggregationMethod.Last,
    };

    private static PlayFab.ProgressionModels.ResetInterval ToSdkResetInterval(ResetInterval interval) => interval switch
    {
        ResetInterval.Manual => PlayFab.ProgressionModels.ResetInterval.Manual,
        ResetInterval.Hour => PlayFab.ProgressionModels.ResetInterval.Hour,
        ResetInterval.Day => PlayFab.ProgressionModels.ResetInterval.Day,
        ResetInterval.Week => PlayFab.ProgressionModels.ResetInterval.Week,
        ResetInterval.Month => PlayFab.ProgressionModels.ResetInterval.Month,
        _ => PlayFab.ProgressionModels.ResetInterval.Manual,
    };

    private sealed class SyncPlan
    {
        public List<StatisticConfig> Creates { get; } = [];
        public List<(StatisticConfig Local, StatisticDefinition Remote, List<string> Changes)> Updates { get; } = [];
        public List<string> Unchanged { get; } = [];
    }
}
