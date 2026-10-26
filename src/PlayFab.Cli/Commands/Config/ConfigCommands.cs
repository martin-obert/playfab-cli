using System.CommandLine;
using PlayFab.Cli.Infrastructure;

namespace PlayFab.Cli.Commands.Config;

/// <summary>Commands for managing persisted CLI credentials.</summary>
public static class ConfigCommands
{
    /// <summary>Registers the 'config' command group with set and show subcommands.</summary>
    public static Command CreateGroup(Option<string?> globalTitleId, Option<string?> globalSecretKey, Option<OutputFormat> outputOption)
    {
        var group = new Command("config", "Manage PlayFab CLI configuration");
        group.Add(CreateSetCommand());
        group.Add(CreateShowCommand(globalTitleId, globalSecretKey, outputOption));
        return group;
    }

    private static Command CreateSetCommand()
    {
        var titleIdOption = new Option<string?>("--title-id") { Description = "PlayFab Title ID to store" };
        var secretKeyOption = new Option<string?>("--secret-key") { Description = "PlayFab Developer Secret Key to store" };

        var command = new Command("set", "Save PlayFab credentials to ~/.playfab/config.json");
        command.Add(titleIdOption);
        command.Add(secretKeyOption);

        command.SetAction(parseResult =>
        {
            var titleId = parseResult.GetValue(titleIdOption);
            var secretKey = parseResult.GetValue(secretKeyOption);

            var config = CliConfig.Load();
            if (titleId is not null) config.TitleId = titleId;
            if (secretKey is not null) config.SecretKey = secretKey;
            config.Save();

            Console.WriteLine("Configuration saved.");
        });

        return command;
    }

    private static Command CreateShowCommand(Option<string?> globalTitleId, Option<string?> globalSecretKey, Option<OutputFormat> outputOption)
    {
        var command = new Command("show", "Display the resolved configuration (secrets masked)");

        command.SetAction(parseResult =>
        {
            var titleId = parseResult.GetValue(globalTitleId);
            var secretKey = parseResult.GetValue(globalSecretKey);
            var output = parseResult.GetValue(outputOption);

            var persisted = CliConfig.Load();
            var resolved = persisted.MergeWith(titleId, secretKey);

            var data = new Dictionary<string, string>
            {
                ["TitleId"] = resolved.TitleId ?? "(not set)",
                ["SecretKey"] = MaskSecret(resolved.SecretKey),
                ["Source: TitleId"] = DescribeSource(titleId, "PLAYFAB_TITLE_ID", persisted.TitleId),
                ["Source: SecretKey"] = DescribeSource(secretKey, "PLAYFAB_SECRET_KEY", persisted.SecretKey),
            };

            if (output == OutputFormat.Table)
                OutputHelper.WriteTable("PlayFab Configuration", data);
            else
                OutputHelper.WriteJson(data);
        });

        return command;
    }

    private static string MaskSecret(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "(not set)";
        if (value.Length <= 4) return new string('*', value.Length);
        return string.Concat(value.AsSpan(0, 4), new string('*', value.Length - 4));
    }

    private static string DescribeSource(string? cliArg, string envVar, string? fileValue)
    {
        if (cliArg is not null) return "CLI argument";
        if (Environment.GetEnvironmentVariable(envVar) is not null) return $"env: {envVar}";
        if (fileValue is not null) return "config file";
        return "not set";
    }
}
