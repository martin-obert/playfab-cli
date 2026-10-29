using System.CommandLine;
using PlayFab;
using PlayFab.Cli.Infrastructure;
using PlayFab.ProgressionModels;
using Spectre.Console;

using SpectreTable = Spectre.Console.Table;

namespace PlayFab.Cli.Commands.Leaderboard;

public static class LeaderboardCommands
{
    public static Command CreateGroup(GlobalOptions globalOptions)
    {
        var group = new Command("leaderboard", "Read back leaderboard rankings and definitions");
        group.Add(CreateGetCommand(globalOptions));
        group.Add(CreateDefinitionCommand(globalOptions));
        return group;
    }

    private static Command CreateGetCommand(GlobalOptions globalOptions)
    {
        var nameOption = new Option<string>("--name")
        {
            Description = "Name of the leaderboard to display",
            Required = true,
        };
        var pageSizeOption = new Option<uint>("--page-size")
        {
            Description = "Maximum number of ranked entries to return",
            DefaultValueFactory = _ => 100,
        };
        var startingPositionOption = new Option<uint?>("--starting-position")
        {
            Description = "Zero-based rank to start reading from; omit to start at rank 0",
        };
        var versionOption = new Option<uint?>("--version")
        {
            Description = "Leaderboard version to read; omit for the current/live version",
        };

        var command = new Command("get", "Read back a leaderboard's ranked entries straight from PlayFab");
        command.Add(nameOption);
        command.Add(pageSizeOption);
        command.Add(startingPositionOption);
        command.Add(versionOption);

        command.SetAction(async parseResult =>
        {
            var titleId = parseResult.GetValue(globalOptions.TitleId);
            var secretKey = parseResult.GetValue(globalOptions.SecretKey);
            var name = parseResult.GetRequiredValue(nameOption);
            var pageSize = parseResult.GetValue(pageSizeOption);
            var startingPosition = parseResult.GetValue(startingPositionOption);
            var version = parseResult.GetValue(versionOption);
            var output = parseResult.GetValue(globalOptions.Output);

            await PlayFabAuth.RequireEntityCredentialsAsync(titleId, secretKey);

            var result = await PlayFabProgressionAPI.GetLeaderboardAsync(new GetEntityLeaderboardRequest
            {
                LeaderboardName = name,
                PageSize = pageSize,
                StartingPosition = startingPosition,
                Version = version,
            });

            if (result.Error is not null)
            {
                Console.Error.WriteLine($"Failed to get leaderboard '{name}': {result.Error.ErrorMessage}");
                return;
            }

            var leaderboard = result.Result;
            var columnNames = (leaderboard.Columns ?? []).Select(c => c.Name).ToList();
            var rankings = leaderboard.Rankings ?? [];

            if (output == OutputFormat.Table)
            {
                var table = new SpectreTable()
                    .Title($"Leaderboard '{name}' (version {leaderboard.Version}, {leaderboard.EntryCount} entries)")
                    .Border(TableBorder.Rounded);
                table.AddColumn("Rank");
                table.AddColumn("Entity ID");
                table.AddColumn("Display Name");
                foreach (var columnName in columnNames)
                    table.AddColumn(columnName);

                foreach (var entry in rankings)
                {
                    var scores = entry.Scores ?? [];
                    var values = columnNames.Select((_, i) => i < scores.Count ? scores[i] : "—");
                    table.AddRow([entry.Rank.ToString(), entry.Entity?.Id ?? "—", entry.DisplayName ?? "—", .. values]);
                }

                AnsiConsole.Write(table);
            }
            else
            {
                OutputHelper.WriteJson(new
                {
                    name,
                    version = leaderboard.Version,
                    entryCount = leaderboard.EntryCount,
                    nextReset = leaderboard.NextReset,
                    columns = columnNames,
                    rankings = rankings.Select(r => new
                    {
                        rank = r.Rank,
                        entityId = r.Entity?.Id,
                        displayName = r.DisplayName,
                        scores = r.Scores,
                        lastUpdated = r.LastUpdated,
                    }),
                });
            }
        });

        return command;
    }

    private static Command CreateDefinitionCommand(GlobalOptions globalOptions)
    {
        var nameOption = new Option<string>("--name")
        {
            Description = "Name of the leaderboard to display",
            Required = true,
        };

        var command = new Command("definition", "Display a leaderboard's definition (columns, sort/link config, versioning) straight from PlayFab");
        command.Add(nameOption);

        command.SetAction(async parseResult =>
        {
            var titleId = parseResult.GetValue(globalOptions.TitleId);
            var secretKey = parseResult.GetValue(globalOptions.SecretKey);
            var name = parseResult.GetRequiredValue(nameOption);
            var output = parseResult.GetValue(globalOptions.Output);

            await PlayFabAuth.RequireEntityCredentialsAsync(titleId, secretKey);

            var result = await PlayFabProgressionAPI.GetLeaderboardDefinitionAsync(new GetLeaderboardDefinitionRequest { Name = name });
            if (result.Error is not null)
            {
                Console.Error.WriteLine($"Failed to get leaderboard definition '{name}': {result.Error.ErrorMessage}");
                return;
            }

            var definition = result.Result;
            var columns = definition.Columns ?? [];

            if (output == OutputFormat.Table)
            {
                var table = new SpectreTable()
                    .Title($"Leaderboard '{name}'")
                    .Border(TableBorder.Rounded);
                table.AddColumn("Field");
                table.AddColumn("Value");
                table.AddRow("Entity Type", definition.EntityType ?? "—");
                table.AddRow("Current Version", definition.Version.ToString());
                table.AddRow("Size Limit", definition.SizeLimit.ToString());
                table.AddRow("Reset Interval", definition.VersionConfiguration?.ResetInterval.ToString() ?? "—");
                table.AddRow("Max Queryable Versions", definition.VersionConfiguration?.MaxQueryableVersions.ToString() ?? "—");
                table.AddRow("Created", definition.Created.ToString("o"));
                table.AddRow("Last Reset", definition.LastResetTime?.ToString("o") ?? "—");
                AnsiConsole.Write(table);

                var columnsTable = new SpectreTable()
                    .Title("Columns")
                    .Border(TableBorder.Rounded);
                columnsTable.AddColumn("Name");
                columnsTable.AddColumn("Sort Direction");
                columnsTable.AddColumn("Linked Statistic");
                foreach (var column in columns)
                {
                    var linked = column.LinkedStatisticColumn is not null
                        ? $"{column.LinkedStatisticColumn.LinkedStatisticName}.{column.LinkedStatisticColumn.LinkedStatisticColumnName}"
                        : "(none)";
                    columnsTable.AddRow(column.Name, column.SortDirection.ToString(), linked);
                }
                AnsiConsole.Write(columnsTable);
            }
            else
            {
                OutputHelper.WriteJson(new
                {
                    name,
                    entityType = definition.EntityType,
                    version = definition.Version,
                    sizeLimit = definition.SizeLimit,
                    resetInterval = definition.VersionConfiguration?.ResetInterval.ToString(),
                    maxQueryableVersions = definition.VersionConfiguration?.MaxQueryableVersions,
                    created = definition.Created,
                    lastResetTime = definition.LastResetTime,
                    columns = columns.Select(c => new
                    {
                        name = c.Name,
                        sortDirection = c.SortDirection.ToString(),
                        linkedStatistic = c.LinkedStatisticColumn?.LinkedStatisticName,
                        linkedStatisticColumn = c.LinkedStatisticColumn?.LinkedStatisticColumnName,
                    }),
                });
            }
        });

        return command;
    }
}
