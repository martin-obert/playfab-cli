using System.CommandLine;
using PlayFab.Cli.Infrastructure;
using PlayFab.AdminModels;
using PlayFab;

namespace PlayFab.Cli.Commands.TitleData;

/// <summary>Commands for reading and writing PlayFab title data key-value pairs.</summary>
public static class TitleDataCommands
{
    /// <summary>Registers the 'titledata' command group with get and set subcommands.</summary>
    public static Command CreateGroup(Option<string?> globalTitleId, Option<string?> globalSecretKey, Option<OutputFormat> outputOption)
    {
        var group = new Command("titledata", "Manage PlayFab title data");
        group.Add(CreateGetCommand(globalTitleId, globalSecretKey, outputOption));
        group.Add(CreateSetCommand(globalTitleId, globalSecretKey));
        return group;
    }

    private static Command CreateGetCommand(Option<string?> globalTitleId, Option<string?> globalSecretKey, Option<OutputFormat> outputOption)
    {
        var keyOption = new Option<string?>("--key") { Description = "Specific key to retrieve (omit to get all)" };

        var command = new Command("get", "Get title data from PlayFab");
        command.Add(keyOption);

        command.SetAction(async parseResult =>
        {
            var titleId = parseResult.GetValue(globalTitleId);
            var secretKey = parseResult.GetValue(globalSecretKey);
            var key = parseResult.GetValue(keyOption);
            var output = parseResult.GetValue(outputOption);

            var config = CliConfig.Load().MergeWith(titleId, secretKey);
            PlayFabClientFactory.Configure(config);

            var request = new GetTitleDataRequest();
            if (key is not null)
                request.Keys = [key];

            var result = await PlayFabAdminAPI.GetTitleDataAsync(request);

            if (result.Error is not null)
            {
                Console.Error.WriteLine($"Error: {result.Error.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            var data = result.Result.Data;

            if (output == OutputFormat.Table)
                OutputHelper.WriteTable("Title Data", data);
            else
                OutputHelper.WriteJson(data);
        });

        return command;
    }

    private static Command CreateSetCommand(Option<string?> globalTitleId, Option<string?> globalSecretKey)
    {
        var keyOption = new Option<string>("--key") { Description = "The title data key", Required = true };
        var valueOption = new Option<string>("--value") { Description = "The value to set (use empty string to delete)", Required = true };

        var command = new Command("set", "Set a title data key-value pair in PlayFab");
        command.Add(keyOption);
        command.Add(valueOption);

        command.SetAction(async parseResult =>
        {
            var titleId = parseResult.GetValue(globalTitleId);
            var secretKey = parseResult.GetValue(globalSecretKey);
            var key = parseResult.GetRequiredValue(keyOption);
            var value = parseResult.GetRequiredValue(valueOption);

            var config = CliConfig.Load().MergeWith(titleId, secretKey);
            PlayFabClientFactory.Configure(config);

            var request = new SetTitleDataRequest
            {
                Key = key,
                Value = value,
            };

            var result = await PlayFabAdminAPI.SetTitleDataAsync(request);

            if (result.Error is not null)
            {
                Console.Error.WriteLine($"Error: {result.Error.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine($"Title data '{key}' updated successfully.");
        });

        return command;
    }
}
