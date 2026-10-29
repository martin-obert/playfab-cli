using System.CommandLine;
using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;
using PlayFab.ProgressionModels;

namespace PlayFab.Cli.Commands.Stats;

public static class StatsCommands
{
    public static Command CreateGroup(GlobalOptions globalOptions)
    {
        var group = new Command("stats", "Seed statistic values for synthetic test players");
        group.Add(CreateSeedCommand(globalOptions));
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
                columns = await StatisticSeedService.GetColumnsAsync(statisticName);
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

            List<SyntheticPlayer> targets;
            if (seedAll)
            {
                targets = store.Players;
            }
            else
            {
                targets = [];
                foreach (var playerId in playerIds)
                {
                    var player = store.Players.Find(p => p.PlayFabId == playerId);
                    if (player is null)
                    {
                        Console.Error.WriteLine($"Player '{playerId}' not found in local tracking file. Only accounts created by this tool can be seeded.");
                        return;
                    }
                    targets.Add(player);
                }
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
}
