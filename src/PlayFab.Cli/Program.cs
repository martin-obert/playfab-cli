using System.CommandLine;
using PlayFab.Cli.Commands.Config;
using PlayFab.Cli.Commands.Migrate;
using PlayFab.Cli.Infrastructure;

var titleIdOption = new Option<string?>("--title-id")
{
    Description = "PlayFab Title ID (overrides env/config)",
    Recursive = true,
};

var secretKeyOption = new Option<string?>("--secret-key")
{
    Description = "PlayFab Developer Secret Key (overrides env/config)",
    Recursive = true,
};

var outputOption = new Option<OutputFormat>("--output", "-o")
{
    Description = "Output format",
    DefaultValueFactory = _ => OutputFormat.Json,
    Recursive = true,
};

var globalOptions = new GlobalOptions(titleIdOption, secretKeyOption, outputOption);

var rootCommand = new RootCommand("PlayFab CLI — configure PlayFab titles from declarative configuration files");
rootCommand.Add(titleIdOption);
rootCommand.Add(secretKeyOption);
rootCommand.Add(outputOption);
rootCommand.Add(ConfigCommands.CreateGroup(globalOptions));
rootCommand.Add(MigrateCommands.CreateGroup(globalOptions));

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
