using System.CommandLine;
using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;
using PlayFab.AdminModels;
using PlayFab;

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
            PlayFabClientFactory.Configure(config);

            var result = await PlayFabAdminAPI.GetPlayerStatisticDefinitionsAsync(
                new GetPlayerStatisticDefinitionsRequest());

            if (result.Error is not null)
            {
                Console.Error.WriteLine($"Error: {result.Error.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            var stats = result.Result.Statistics ?? [];
            var output = parseResult.GetValue(outputOption);

            if (output == OutputFormat.Table)
            {
                var rows = new Dictionary<string, string>();
                foreach (var s in stats)
                    rows[s.StatisticName] = $"{s.AggregationMethod} | reset: {s.VersionChangeInterval} | v{s.CurrentVersion}";
                OutputHelper.WriteTable($"Statistics ({stats.Count})", rows);
            }
            else
            {
                OutputHelper.WriteJson(stats.Select(s => new
                {
                    name = s.StatisticName,
                    aggregationMethod = s.AggregationMethod?.ToString(),
                    resetInterval = s.VersionChangeInterval?.ToString(),
                    version = s.CurrentVersion,
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

            var localStats = schema.Statistics.AsEnumerable();
            if (nameFilter is not null)
            {
                localStats = localStats.Where(s => s.Name.Equals(nameFilter, StringComparison.OrdinalIgnoreCase));
                if (!localStats.Any())
                {
                    Console.Error.WriteLine($"Statistic '{nameFilter}' not found in schema file.");
                    Environment.ExitCode = 1;
                    return;
                }
            }

            var config = CliConfig.Load().MergeWith(
                parseResult.GetValue(globalTitleId),
                parseResult.GetValue(globalSecretKey));
            PlayFabClientFactory.Configure(config);

            var remoteResult = await PlayFabAdminAPI.GetPlayerStatisticDefinitionsAsync(
                new GetPlayerStatisticDefinitionsRequest());

            if (remoteResult.Error is not null)
            {
                Console.Error.WriteLine($"Error fetching remote definitions: {remoteResult.Error.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            var remoteLookup = (remoteResult.Result.Statistics ?? [])
                .ToDictionary(s => s.StatisticName, StringComparer.OrdinalIgnoreCase);

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

    private static SyncPlan BuildSyncPlan(IEnumerable<StatisticConfig> localStats, Dictionary<string, PlayerStatisticDefinition> remoteLookup)
    {
        var plan = new SyncPlan();

        foreach (var local in localStats)
        {
            if (!remoteLookup.TryGetValue(local.Name, out var remote))
            {
                plan.Creates.Add(local);
                continue;
            }

            var remoteAgg = remote.AggregationMethod?.ToString() ?? "Last";
            var remoteReset = remote.VersionChangeInterval?.ToString() ?? "Never";

            if (!remoteAgg.Equals(local.AggregationMethod.ToString(), StringComparison.OrdinalIgnoreCase)
                || !remoteReset.Equals(local.ResetInterval.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                plan.Updates.Add((local, remote));
            }
            else
            {
                plan.Unchanged.Add(local.Name);
            }
        }

        return plan;
    }

    private static void PrintPlan(SyncPlan plan, OutputFormat output)
    {
        if (output == OutputFormat.Json)
        {
            OutputHelper.WriteJson(new
            {
                create = plan.Creates.Select(c => c.Name),
                update = plan.Updates.Select(u => new
                {
                    name = u.Local.Name,
                    changes = FormatChanges(u.Local, u.Remote),
                }),
                unchanged = plan.Unchanged,
            });
            return;
        }

        if (plan.Creates.Count > 0)
        {
            Console.WriteLine("CREATE:");
            foreach (var c in plan.Creates)
                Console.WriteLine($"  + {c.Name} (aggregation: {c.AggregationMethod}, reset: {c.ResetInterval})");
        }

        if (plan.Updates.Count > 0)
        {
            Console.WriteLine("UPDATE:");
            foreach (var (local, remote) in plan.Updates)
            {
                Console.WriteLine($"  ~ {local.Name}");
                foreach (var change in FormatChanges(local, remote))
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

    private static List<string> FormatChanges(StatisticConfig local, PlayerStatisticDefinition remote)
    {
        var changes = new List<string>();
        var remoteAgg = remote.AggregationMethod?.ToString() ?? "Last";
        var remoteReset = remote.VersionChangeInterval?.ToString() ?? "Never";

        if (!remoteAgg.Equals(local.AggregationMethod.ToString(), StringComparison.OrdinalIgnoreCase))
            changes.Add($"aggregationMethod: {remoteAgg} → {local.AggregationMethod}");

        if (!remoteReset.Equals(local.ResetInterval.ToString(), StringComparison.OrdinalIgnoreCase))
            changes.Add($"resetInterval: {remoteReset} → {local.ResetInterval}");

        return changes;
    }

    private static async Task ApplyPlan(SyncPlan plan)
    {
        foreach (var stat in plan.Creates)
        {
            var result = await PlayFabAdminAPI.CreatePlayerStatisticDefinitionAsync(
                new CreatePlayerStatisticDefinitionRequest
                {
                    StatisticName = stat.Name,
                    AggregationMethod = ToSdkAggregation(stat.AggregationMethod),
                    VersionChangeInterval = ToSdkResetInterval(stat.ResetInterval),
                });

            if (result.Error is not null)
            {
                Console.Error.WriteLine($"Failed to create '{stat.Name}': {result.Error.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine($"Created: {stat.Name}");
        }

        foreach (var (local, _) in plan.Updates)
        {
            var result = await PlayFabAdminAPI.UpdatePlayerStatisticDefinitionAsync(
                new UpdatePlayerStatisticDefinitionRequest
                {
                    StatisticName = local.Name,
                    AggregationMethod = ToSdkAggregation(local.AggregationMethod),
                    VersionChangeInterval = ToSdkResetInterval(local.ResetInterval),
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

    private static StatisticAggregationMethod ToSdkAggregation(AggregationMethod method) => method switch
    {
        AggregationMethod.Last => StatisticAggregationMethod.Last,
        AggregationMethod.Min => StatisticAggregationMethod.Min,
        AggregationMethod.Max => StatisticAggregationMethod.Max,
        AggregationMethod.Sum => StatisticAggregationMethod.Sum,
        _ => StatisticAggregationMethod.Last,
    };

    private static StatisticResetIntervalOption ToSdkResetInterval(ResetInterval interval) => interval switch
    {
        ResetInterval.Never => StatisticResetIntervalOption.Never,
        ResetInterval.Hour => StatisticResetIntervalOption.Hour,
        ResetInterval.Day => StatisticResetIntervalOption.Day,
        ResetInterval.Week => StatisticResetIntervalOption.Week,
        ResetInterval.Month => StatisticResetIntervalOption.Month,
        _ => StatisticResetIntervalOption.Never,
    };

    private sealed class SyncPlan
    {
        public List<StatisticConfig> Creates { get; } = [];
        public List<(StatisticConfig Local, PlayerStatisticDefinition Remote)> Updates { get; } = [];
        public List<string> Unchanged { get; } = [];
    }
}
