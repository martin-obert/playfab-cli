using System.CommandLine;
using PlayFab.Cli.Infrastructure;
using Spectre.Console;

using SpectreTable = Spectre.Console.Table;

namespace PlayFab.Cli.Commands.Players;

public static class PlayersCommands
{
    public static Command CreateGroup(GlobalOptions globalOptions)
    {
        var group = new Command("players", "Manage synthetic test player accounts");
        group.Add(CreateCreateCommand(globalOptions));
        group.Add(CreateDeleteCommand(globalOptions));
        group.Add(CreateListCommand(globalOptions));
        return group;
    }

    private static Command CreateCreateCommand(GlobalOptions globalOptions)
    {
        var countOption = new Option<int>("--count")
        {
            Description = "Number of synthetic accounts to create (max 100)",
            DefaultValueFactory = _ => 1,
        };

        var command = new Command("create", "Create synthetic test player accounts with random display names");
        command.Add(countOption);

        command.SetAction(async parseResult =>
        {
            var titleId = parseResult.GetValue(globalOptions.TitleId);
            var secretKey = parseResult.GetValue(globalOptions.SecretKey);
            var count = parseResult.GetValue(countOption);

            if (count < 1 || count > 100)
            {
                Console.Error.WriteLine("--count must be between 1 and 100.");
                return;
            }

            var creds = PlayFabAuth.RequireCredentials(titleId, secretKey);
            var store = SyntheticPlayerService.Load(creds.TitleId!);

            var created = 0;
            for (var i = 0; i < count; i++)
            {
                var displayName = SyntheticPlayerService.GenerateDisplayName();
                Console.Write($"Creating account {i + 1}/{count} ({displayName})...");

                try
                {
                    var player = await SyntheticPlayerService.CreateAccountAsync(displayName);
                    store.Players.Add(player);
                    SyntheticPlayerService.Save(store);
                    Console.WriteLine(" done.");
                    created++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" FAILED: {ex.Message}");
                    break;
                }

                if (count > 10)
                    await Task.Delay(100);
            }

            Console.WriteLine($"{created}/{count} account(s) created.");
        });

        return command;
    }

    private static Command CreateDeleteCommand(GlobalOptions globalOptions)
    {
        var allOption = new Option<bool>("--all")
        {
            Description = "Delete all tracked synthetic accounts for this title",
        };
        var playerIdOption = new Option<string?>("--player-id")
        {
            Description = "Delete a specific tracked synthetic account by PlayFab ID",
        };

        var command = new Command("delete", "Delete previously created synthetic accounts");
        command.Add(allOption);
        command.Add(playerIdOption);

        command.SetAction(async parseResult =>
        {
            var titleId = parseResult.GetValue(globalOptions.TitleId);
            var secretKey = parseResult.GetValue(globalOptions.SecretKey);
            var deleteAll = parseResult.GetValue(allOption);
            var playerId = parseResult.GetValue(playerIdOption);

            if (!deleteAll && playerId is null)
            {
                Console.Error.WriteLine("Specify --all or --player-id <id>.");
                return;
            }

            var creds = PlayFabAuth.RequireCredentials(titleId, secretKey);
            var store = SyntheticPlayerService.Load(creds.TitleId!);

            if (deleteAll)
            {
                var total = store.Players.Count;
                if (total == 0)
                {
                    Console.WriteLine("No tracked synthetic accounts to delete.");
                    return;
                }

                var deleted = 0;
                for (var i = store.Players.Count - 1; i >= 0; i--)
                {
                    var player = store.Players[i];
                    Console.Write($"Deleting {player.PlayFabId} ({player.DisplayName})...");

                    try
                    {
                        await SyntheticPlayerService.DeleteAccountAsync(player.PlayFabId);
                        store.Players.RemoveAt(i);
                        SyntheticPlayerService.Save(store);
                        Console.WriteLine(" done.");
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($" FAILED: {ex.Message}");
                        break;
                    }

                    if (total > 10)
                        await Task.Delay(100);
                }

                Console.WriteLine($"{deleted}/{total} account(s) deleted.");
            }
            else
            {
                var player = store.Players.Find(p => p.PlayFabId == playerId);
                if (player is null)
                {
                    Console.Error.WriteLine($"Player '{playerId}' not found in local tracking file. Only accounts created by this tool can be deleted.");
                    return;
                }

                Console.Write($"Deleting {player.PlayFabId} ({player.DisplayName})...");

                try
                {
                    await SyntheticPlayerService.DeleteAccountAsync(player.PlayFabId);
                    store.Players.Remove(player);
                    SyntheticPlayerService.Save(store);
                    Console.WriteLine(" done.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" FAILED: {ex.Message}");
                }
            }
        });

        return command;
    }

    private static Command CreateListCommand(GlobalOptions globalOptions)
    {
        var command = new Command("list", "List locally tracked synthetic accounts for the current title");

        command.SetAction(parseResult =>
        {
            var titleId = parseResult.GetValue(globalOptions.TitleId);
            var secretKey = parseResult.GetValue(globalOptions.SecretKey);
            var output = parseResult.GetValue(globalOptions.Output);

            var creds = PlayFabAuth.RequireCredentials(titleId, secretKey);
            var store = SyntheticPlayerService.Load(creds.TitleId!);

            if (store.Players.Count == 0)
            {
                Console.WriteLine("No tracked synthetic accounts.");
                return;
            }

            if (output == OutputFormat.Table)
            {
                var table = new SpectreTable()
                    .Title("Synthetic Players")
                    .Border(TableBorder.Rounded);
                table.AddColumn("PlayFab ID");
                table.AddColumn("Custom ID");
                table.AddColumn("Display Name");
                table.AddColumn("Created At");

                foreach (var player in store.Players)
                    table.AddRow(player.PlayFabId, player.CustomId, player.DisplayName, player.CreatedAt.ToString("o"));

                AnsiConsole.Write(table);
            }
            else
            {
                OutputHelper.WriteJson(store.Players);
            }
        });

        return command;
    }
}
