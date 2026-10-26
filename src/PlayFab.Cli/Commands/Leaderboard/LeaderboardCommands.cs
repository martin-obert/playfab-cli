using System.CommandLine;
using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;

namespace PlayFab.Cli.Commands.Leaderboard;

/// <summary>Commands for managing PlayFab statistics and leaderboards via Progression API.</summary>
public static class LeaderboardCommands
{
    /// <summary>Registers the 'leaderboard' command group with list, validate, and sync subcommands.</summary>
    public static Command CreateGroup(Option<string?> globalTitleId, Option<string?> globalSecretKey, Option<OutputFormat> outputOption)
    {
        var group = new Command("leaderboard", "Manage PlayFab statistics and leaderboards from a local schema file");
        group.Add(CreateListCommand(globalTitleId, globalSecretKey, outputOption));
        group.Add(CreateValidateCommand());
        group.Add(CreateSyncCommand(globalTitleId, globalSecretKey, outputOption));
        return group;
    }

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
            var stats = await ProgressionApiClient.FetchAllStatisticDefinitions();
            var leaderboards = await ProgressionApiClient.FetchAllLeaderboardDefinitions();

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

            var remoteStats = await ProgressionApiClient.FetchAllStatisticDefinitions();
            var remoteStatLookup = remoteStats.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

            var remoteLeaderboards = await ProgressionApiClient.FetchAllLeaderboardDefinitions();
            var remoteLbLookup = remoteLeaderboards.ToDictionary(lb => lb.Name, StringComparer.OrdinalIgnoreCase);

            var localStats = SyncEngine.FilterByName(schema.Statistics, s => s.Name, nameFilter);
            var localLbs = SyncEngine.FilterByName(schema.Leaderboards, lb => lb.Name, nameFilter);

            if (nameFilter is not null && localStats.Count == 0 && localLbs.Count == 0)
            {
                Console.Error.WriteLine($"'{nameFilter}' not found in schema file.");
                Environment.ExitCode = 1;
                return;
            }

            var statPlan = SyncEngine.BuildStatisticSyncPlan(localStats, remoteStatLookup);
            var lbPlan = SyncEngine.BuildLeaderboardSyncPlan(localLbs, remoteLbLookup);

            SyncEngine.PrintSyncPlan(statPlan, lbPlan, output);

            var hasChanges = statPlan.Creates.Count + statPlan.Updates.Count + lbPlan.Creates.Count + lbPlan.Updates.Count > 0;

            if (!apply)
            {
                if (hasChanges)
                    Console.WriteLine("\nRun with --apply to push changes.");
                return;
            }

            await SyncEngine.ApplyStatisticPlan(statPlan);
            await SyncEngine.ApplyLeaderboardPlan(lbPlan);
        });

        return command;
    }
}
