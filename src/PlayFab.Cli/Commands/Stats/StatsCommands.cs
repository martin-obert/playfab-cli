using System.CommandLine;
using PlayFab;
using PlayFab.Cli.Infrastructure;
using PlayFab.ProgressionModels;
using Spectre.Console;

using SpectreTable = Spectre.Console.Table;

namespace PlayFab.Cli.Commands.Stats;

public static class StatsCommands
{
    public static Command CreateGroup(GlobalOptions globalOptions)
    {
        var statisticsDirOption = new Option<string>("--statistics-dir")
        {
            Description = "Path to statistic definitions directory",
            DefaultValueFactory = _ => "statistics",
            Recursive = true,
        };

        var group = new Command("stats", "Seed and read back statistic values for synthetic test players");
        group.Add(statisticsDirOption);
        group.Add(CreateSeedCommand(globalOptions));
        group.Add(CreateGetCommand(globalOptions));
        group.Add(CreateDefinitionCommand(globalOptions));
        group.Add(CreatePullCommand(globalOptions, statisticsDirOption));
        return group;
    }

    private static Command CreateSeedCommand(GlobalOptions globalOptions)
    {
        var statisticOption = new Option<string>("--statistic")
        {
            Description = "Name of the target statistic",
            Required = true,
        };
        var columnOption = new Option<string[]>("--column")
        {
            Description = "Column to seed, format 'Name:min-max' (e.g. Score:100-2000). Repeatable.",
            DefaultValueFactory = _ => [],
        };
        var allOption = new Option<bool>("--all")
        {
            Description = "Seed all tracked synthetic accounts for this title",
        };
        var playerIdOption = new Option<string[]>("--player-id")
        {
            Description = "Seed a specific tracked synthetic account by PlayFab ID. Repeatable.",
            DefaultValueFactory = _ => [],
        };
        var countOption = new Option<int>("--count")
        {
            Description = "Number of independent randomized writes per player (e.g. to build up a Sum total or try several Min/Max rolls)",
            DefaultValueFactory = _ => 1,
        };

        var command = new Command("seed", "Write randomized values into selected columns of a statistic for synthetic test players");
        command.Add(statisticOption);
        command.Add(columnOption);
        command.Add(allOption);
        command.Add(playerIdOption);
        command.Add(countOption);

        command.SetAction(async parseResult =>
        {
            var titleId = parseResult.GetValue(globalOptions.TitleId);
            var secretKey = parseResult.GetValue(globalOptions.SecretKey);
            var statisticName = parseResult.GetRequiredValue(statisticOption);
            var columnSpecs = parseResult.GetValue(columnOption) ?? [];
            var seedAll = parseResult.GetValue(allOption);
            var playerIds = parseResult.GetValue(playerIdOption) ?? [];
            var count = parseResult.GetValue(countOption);

            if (columnSpecs.Length == 0)
            {
                Console.Error.WriteLine("Specify at least one --column <Name:min-max>.");
                return;
            }

            if (count < 1)
            {
                Console.Error.WriteLine("--count must be >= 1.");
                return;
            }

            if (!seedAll && playerIds.Length == 0)
            {
                Console.Error.WriteLine("Specify --all or --player-id <id>.");
                return;
            }

            var columnRanges = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);
            foreach (var spec in columnSpecs)
            {
                try
                {
                    var (name, min, max) = StatisticSeedService.ParseColumnSpec(spec);
                    columnRanges[name] = (min, max);
                }
                catch (FormatException ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return;
                }
            }

            var creds = await PlayFabAuth.RequireEntityCredentialsAsync(titleId, secretKey);

            List<StatisticColumn> columns;
            try
            {
                (columns, _) = await StatisticSeedService.GetColumnsAsync(statisticName);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load statistic '{statisticName}': {ex.Message}");
                Console.Error.WriteLine("Create it first via a 'create-statistic' migration.");
                return;
            }

            var columnNames = columns.Select(c => c.Name).ToList();
            var requestedColumnNames = columnRanges.Keys.ToList();

            var columnErrors = StatisticSeedService.ValidateColumns(columnNames, requestedColumnNames);
            if (columnErrors.Count > 0)
            {
                foreach (var error in columnErrors)
                    Console.Error.WriteLine(error);
                return;
            }

            var sumColumnErrors = StatisticSeedService.ValidateUntouchedSumColumns(columns, requestedColumnNames);
            if (sumColumnErrors.Count > 0)
            {
                foreach (var error in sumColumnErrors)
                    Console.Error.WriteLine(error);
                return;
            }

            var store = SyntheticPlayerService.Load(creds.TitleId!);
            var (targets, targetError) = StatisticSeedService.ResolvePlayerTargets(store.Players, seedAll, playerIds);
            if (targetError is not null)
            {
                Console.Error.WriteLine(targetError);
                return;
            }

            if (targets.Count == 0)
            {
                Console.WriteLine("No tracked synthetic accounts to seed.");
                return;
            }

            var countSuffix = count > 1 ? $" x{count}" : "";
            var seeded = 0;
            for (var i = 0; i < targets.Count; i++)
            {
                var player = targets[i];
                Console.Write($"Seeding account {i + 1}/{targets.Count} ({player.PlayFabId}){countSuffix}...");

                var failed = false;
                for (var w = 0; w < count; w++)
                {
                    try
                    {
                        await StatisticSeedService.SeedPlayerAsync(player.PlayFabId, statisticName, columnNames, columnRanges);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($" FAILED on write {w + 1}/{count}: {ex.Message}");
                        failed = true;
                        break;
                    }

                    if (count > 10)
                        await Task.Delay(100);
                }

                if (failed)
                    break;

                Console.WriteLine(" done.");
                seeded++;

                if (targets.Count > 10)
                    await Task.Delay(100);
            }

            Console.WriteLine($"{seeded}/{targets.Count} account(s) seeded.");
        });

        return command;
    }

    private static Command CreateGetCommand(GlobalOptions globalOptions)
    {
        var statisticOption = new Option<string>("--statistic")
        {
            Description = "Name of the target statistic",
            Required = true,
        };
        var allOption = new Option<bool>("--all")
        {
            Description = "Read all tracked synthetic accounts for this title",
        };
        var playerIdOption = new Option<string[]>("--player-id")
        {
            Description = "Read a specific tracked synthetic account by PlayFab ID. Repeatable.",
            DefaultValueFactory = _ => [],
        };

        var command = new Command("get", "Read back a statistic's current stored value for synthetic test players, straight from PlayFab");
        command.Add(statisticOption);
        command.Add(allOption);
        command.Add(playerIdOption);

        command.SetAction(async parseResult =>
        {
            var titleId = parseResult.GetValue(globalOptions.TitleId);
            var secretKey = parseResult.GetValue(globalOptions.SecretKey);
            var statisticName = parseResult.GetRequiredValue(statisticOption);
            var readAll = parseResult.GetValue(allOption);
            var playerIds = parseResult.GetValue(playerIdOption) ?? [];
            var output = parseResult.GetValue(globalOptions.Output);

            if (!readAll && playerIds.Length == 0)
            {
                Console.Error.WriteLine("Specify --all or --player-id <id>.");
                return;
            }

            var creds = await PlayFabAuth.RequireEntityCredentialsAsync(titleId, secretKey);

            List<StatisticColumn> columns;
            uint currentVersion;
            try
            {
                (columns, currentVersion) = await StatisticSeedService.GetColumnsAsync(statisticName);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load statistic '{statisticName}': {ex.Message}");
                return;
            }

            var columnNames = columns.Select(c => c.Name).ToList();

            var store = SyntheticPlayerService.Load(creds.TitleId!);
            var (targets, targetError) = StatisticSeedService.ResolvePlayerTargets(store.Players, readAll, playerIds);
            if (targetError is not null)
            {
                Console.Error.WriteLine(targetError);
                return;
            }

            if (targets.Count == 0)
            {
                Console.WriteLine("No tracked synthetic accounts to read.");
                return;
            }

            var rows = new List<(string PlayFabId, string Version, string[] Values)>();
            foreach (var player in targets)
            {
                var value = await StatisticSeedService.GetPlayerStatisticAsync(player.PlayFabId, statisticName);
                var scores = value?.Scores ?? [];
                var values = columnNames.Select((_, i) => i < scores.Count ? scores[i] : "—").ToArray();
                rows.Add((player.PlayFabId, value is not null ? value.Version.ToString() : "—", values));
            }

            Console.WriteLine($"Statistic '{statisticName}' current definition version: {currentVersion}");
            var mismatches = rows.Where(r => r.Version != "—" && uint.Parse(r.Version) != currentVersion).ToList();
            if (mismatches.Count > 0)
            {
                Console.WriteLine($"WARNING: {mismatches.Count} player(s) have a value recorded under a version other than the current one — it may not surface wherever Game Manager looks for the current version's data:");
                foreach (var m in mismatches)
                    Console.WriteLine($"  {m.PlayFabId}: value version {m.Version} != current version {currentVersion}");
            }

            if (output == OutputFormat.Table)
            {
                var table = new SpectreTable()
                    .Title($"Statistic '{statisticName}'")
                    .Border(TableBorder.Rounded);
                table.AddColumn("PlayFab ID");
                table.AddColumn("Version");
                foreach (var columnName in columnNames)
                    table.AddColumn(columnName);

                foreach (var row in rows)
                    table.AddRow([row.PlayFabId, row.Version, .. row.Values]);

                AnsiConsole.Write(table);
            }
            else
            {
                OutputHelper.WriteJson(rows.Select(r => new
                {
                    playFabId = r.PlayFabId,
                    version = r.Version,
                    columns = columnNames.Zip(r.Values, (name, value) => (name, value))
                        .ToDictionary(x => x.name, x => x.value),
                }));
            }
        });

        return command;
    }

    private static Command CreateDefinitionCommand(GlobalOptions globalOptions)
    {
        var statisticOption = new Option<string>("--statistic")
        {
            Description = "Name of the statistic to display",
            Required = true,
        };

        var command = new Command("definition", "Display a statistic's definition (columns, aggregation methods, versioning) straight from PlayFab");
        command.Add(statisticOption);

        command.SetAction(async parseResult =>
        {
            var titleId = parseResult.GetValue(globalOptions.TitleId);
            var secretKey = parseResult.GetValue(globalOptions.SecretKey);
            var statisticName = parseResult.GetRequiredValue(statisticOption);
            var output = parseResult.GetValue(globalOptions.Output);

            await PlayFabAuth.RequireEntityCredentialsAsync(titleId, secretKey);

            GetStatisticDefinitionResponse definition;
            try
            {
                definition = await StatisticSeedService.GetDefinitionAsync(statisticName);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load statistic '{statisticName}': {ex.Message}");
                return;
            }

            var columns = definition.Columns ?? [];
            var linkedLeaderboards = definition.LinkedLeaderboardNames ?? [];

            if (output == OutputFormat.Table)
            {
                var table = new SpectreTable()
                    .Title($"Statistic '{statisticName}'")
                    .Border(TableBorder.Rounded);
                table.AddColumn("Field");
                table.AddColumn("Value");
                table.AddRow("Entity Type", definition.EntityType ?? "—");
                table.AddRow("Current Version", definition.Version.ToString());
                table.AddRow("Reset Interval", definition.VersionConfiguration?.ResetInterval.ToString() ?? "—");
                table.AddRow("Max Queryable Versions", definition.VersionConfiguration?.MaxQueryableVersions.ToString() ?? "—");
                table.AddRow("Created", definition.Created.ToString("o"));
                table.AddRow("Last Reset", definition.LastResetTime?.ToString("o") ?? "—");
                table.AddRow("Linked Leaderboards", linkedLeaderboards.Count > 0 ? string.Join(", ", linkedLeaderboards) : "(none)");
                AnsiConsole.Write(table);

                var columnsTable = new SpectreTable()
                    .Title("Columns")
                    .Border(TableBorder.Rounded);
                columnsTable.AddColumn("Name");
                columnsTable.AddColumn("Aggregation Method");
                foreach (var column in columns)
                    columnsTable.AddRow(column.Name, column.AggregationMethod.ToString());
                AnsiConsole.Write(columnsTable);
            }
            else
            {
                OutputHelper.WriteJson(new
                {
                    name = statisticName,
                    entityType = definition.EntityType,
                    version = definition.Version,
                    resetInterval = definition.VersionConfiguration?.ResetInterval.ToString(),
                    maxQueryableVersions = definition.VersionConfiguration?.MaxQueryableVersions,
                    created = definition.Created,
                    lastResetTime = definition.LastResetTime,
                    linkedLeaderboardNames = linkedLeaderboards,
                    columns = columns.Select(c => new { name = c.Name, aggregationMethod = c.AggregationMethod.ToString() }),
                });
            }
        });

        return command;
    }

    private static Command CreatePullCommand(GlobalOptions globalOptions, Option<string> statisticsDirOption)
    {
        var statisticOption = new Option<string?>("--statistic")
        {
            Description = "Name of a single statistic to pull",
        };
        var allOption = new Option<bool>("--all")
        {
            Description = "Pull all statistic definitions from PlayFab",
        };

        var command = new Command("pull", "Pull statistic definitions from PlayFab and save as YAML files");
        command.Add(statisticOption);
        command.Add(allOption);

        command.SetAction(async parseResult =>
        {
            var titleId = parseResult.GetValue(globalOptions.TitleId);
            var secretKey = parseResult.GetValue(globalOptions.SecretKey);
            var statisticName = parseResult.GetValue(statisticOption);
            var pullAll = parseResult.GetValue(allOption);
            var statisticsDir = parseResult.GetValue(statisticsDirOption)!;

            if (!pullAll && statisticName is null)
            {
                Console.Error.WriteLine("Specify --statistic <name> or --all.");
                return;
            }

            await PlayFabAuth.RequireEntityCredentialsAsync(titleId, secretKey);

            if (pullAll)
            {
                var definitions = new List<StatisticDefinition>();
                string? skipToken = null;

                do
                {
                    var result = await PlayFabProgressionAPI.ListStatisticDefinitionsAsync(
                        new ListStatisticDefinitionsRequest { SkipToken = skipToken });

                    if (result.Error is not null)
                    {
                        Console.Error.WriteLine($"ListStatisticDefinitions failed: {result.Error.ErrorMessage}");
                        return;
                    }

                    definitions.AddRange(result.Result.StatisticDefinitions ?? []);
                    skipToken = result.Result.SkipToken;
                } while (skipToken is not null);

                if (definitions.Count == 0)
                {
                    Console.WriteLine("No statistic definitions found.");
                    return;
                }

                var saved = 0;
                foreach (var definition in definitions)
                {
                    var mapped = StatisticDefinitionStore.MapFromPlayFab(
                        new GetStatisticDefinitionResponse
                        {
                            Name = definition.Name,
                            EntityType = definition.EntityType,
                            Columns = definition.Columns,
                            VersionConfiguration = definition.VersionConfiguration,
                        });

                    StatisticDefinitionStore.Save(mapped, statisticsDir);
                    Console.WriteLine($"  Saved {mapped.Name}.yml");
                    saved++;
                }

                Console.WriteLine($"{saved} statistic definition(s) pulled to '{statisticsDir}/'.");
            }
            else
            {
                GetStatisticDefinitionResponse response;
                try
                {
                    response = await StatisticSeedService.GetDefinitionAsync(statisticName!);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to get statistic '{statisticName}': {ex.Message}");
                    return;
                }

                var mapped = StatisticDefinitionStore.MapFromPlayFab(response);
                StatisticDefinitionStore.Save(mapped, statisticsDir);
                Console.WriteLine($"Saved {mapped.Name}.yml to '{statisticsDir}/'.");
            }
        });

        return command;
    }
}
