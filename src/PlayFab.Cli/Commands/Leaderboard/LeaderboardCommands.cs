using System.CommandLine;
using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;
using PlayFab.ProgressionModels;

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
        group.Add(CreateResetCommand(globalTitleId, globalSecretKey));
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

    private static Command CreateResetCommand(Option<string?> globalTitleId, Option<string?> globalSecretKey)
    {
        var nameOption = new Option<string>("--name") { Description = "Name of the statistic or leaderboard to reset", Required = true };
        var typeOption = new Option<string?>("--type") { Description = "Disambiguate when a statistic and leaderboard share the same name: 'statistic' or 'leaderboard'" };
        var versionOption = new Option<uint?>("--version") { Description = "Only reset if the current remote version matches this value; omit to reset whatever the latest version is" };
        var applyOption = new Option<bool>("--apply") { Description = "Apply the reset (default is dry-run)", DefaultValueFactory = _ => false };

        var command = new Command("reset", "Manually reset a statistic or leaderboard by incrementing its version");
        command.Add(nameOption);
        command.Add(typeOption);
        command.Add(versionOption);
        command.Add(applyOption);

        command.SetAction(async parseResult =>
        {
            var name = parseResult.GetRequiredValue(nameOption);
            var typeFilter = parseResult.GetValue(typeOption);
            var expectedVersion = parseResult.GetValue(versionOption);
            var apply = parseResult.GetValue(applyOption);

            if (typeFilter is not null
                && !typeFilter.Equals("statistic", StringComparison.OrdinalIgnoreCase)
                && !typeFilter.Equals("leaderboard", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("--type must be 'statistic' or 'leaderboard'.");
                Environment.ExitCode = 1;
                return;
            }

            var config = CliConfig.Load().MergeWith(
                parseResult.GetValue(globalTitleId),
                parseResult.GetValue(globalSecretKey));
            await PlayFabClientFactory.AuthenticateEntityAsync(config);

            var stats = await ProgressionApiClient.FetchAllStatisticDefinitions();
            var leaderboards = await ProgressionApiClient.FetchAllLeaderboardDefinitions();

            var stat = typeFilter is null || typeFilter.Equals("statistic", StringComparison.OrdinalIgnoreCase)
                ? stats.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                : null;
            var lb = typeFilter is null || typeFilter.Equals("leaderboard", StringComparison.OrdinalIgnoreCase)
                ? leaderboards.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                : null;

            if (stat is null && lb is null)
            {
                Console.Error.WriteLine($"'{name}' not found among remote statistics or leaderboards.");
                Environment.ExitCode = 1;
                return;
            }

            if (stat is not null && lb is not null)
            {
                Console.Error.WriteLine($"'{name}' matches both a statistic and a leaderboard. Use --type statistic or --type leaderboard to disambiguate.");
                Environment.ExitCode = 1;
                return;
            }

            var currentVersion = stat?.Version ?? lb!.Version;
            if (expectedVersion is not null && expectedVersion != currentVersion)
            {
                Console.Error.WriteLine($"Expected version {expectedVersion} but current version of '{name}' is {currentVersion}. Aborting.");
                Environment.ExitCode = 1;
                return;
            }

            if (lb is not null)
            {
                var linkedStatNames = (lb.Columns ?? [])
                    .Select(c => c.LinkedStatisticColumn?.LinkedStatisticName)
                    .Where(n => n is not null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (linkedStatNames.Count > 0)
                {
                    Console.WriteLine($"Leaderboard '{lb.Name}' is linked to statistic '{string.Join(", ", linkedStatNames)}'.");
                    Console.WriteLine("PlayFab only allows resetting the underlying statistic directly — doing so also resets this leaderboard.");

                    foreach (var statName in linkedStatNames)
                    {
                        var linkedStat = stats.FirstOrDefault(s => s.Name.Equals(statName, StringComparison.OrdinalIgnoreCase));
                        if (linkedStat is null)
                        {
                            Console.Error.WriteLine($"Linked statistic '{statName}' not found remotely; cannot reset.");
                            Environment.ExitCode = 1;
                            continue;
                        }

                        await ResetStatistic(linkedStat, apply);
                    }

                    return;
                }

                await ResetLeaderboard(lb, apply);
                return;
            }

            await ResetStatistic(stat!, apply);
        });

        return command;
    }

    private static async Task ResetStatistic(StatisticDefinition stat, bool apply)
    {
        var linked = stat.LinkedLeaderboardNames ?? [];
        var linkedNote = linked.Count > 0 ? $" (also resets linked leaderboard(s): {string.Join(", ", linked)})" : "";

        if (!apply)
        {
            Console.WriteLine($"Would reset statistic '{stat.Name}' (version {stat.Version} → {stat.Version + 1}){linkedNote}.");
            Console.WriteLine("Run with --apply to perform the reset.");
            return;
        }

        var newVersion = await ProgressionApiClient.IncrementStatisticVersion(stat.Name);
        if (newVersion is null) return;

        Console.WriteLine($"Reset statistic '{stat.Name}' → version {newVersion}{linkedNote}.");
    }

    private static async Task ResetLeaderboard(LeaderboardDefinition lb, bool apply)
    {
        if (!apply)
        {
            Console.WriteLine($"Would reset leaderboard '{lb.Name}' (version {lb.Version} → {lb.Version + 1}).");
            Console.WriteLine("Run with --apply to perform the reset.");
            return;
        }

        var newVersion = await ProgressionApiClient.IncrementLeaderboardVersion(lb.Name);
        if (newVersion is null) return;

        Console.WriteLine($"Reset leaderboard '{lb.Name}' → version {newVersion}.");
    }
}
