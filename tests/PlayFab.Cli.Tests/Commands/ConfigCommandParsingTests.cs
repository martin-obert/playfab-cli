using System.CommandLine;
using PlayFab.Cli.Commands.Config;
using PlayFab.Cli.Infrastructure;
using Xunit;

namespace PlayFab.Cli.Tests.Commands;

public class ConfigCommandParsingTests
{
    private readonly RootCommand _rootCommand;
    private readonly GlobalOptions _globalOptions;

    public ConfigCommandParsingTests()
    {
        var titleIdOption = new Option<string?>("--title-id") { Recursive = true };
        var secretKeyOption = new Option<string?>("--secret-key") { Recursive = true };
        var outputOption = new Option<OutputFormat>("--output", "-o")
        {
            DefaultValueFactory = _ => OutputFormat.Json,
            Recursive = true,
        };

        _globalOptions = new GlobalOptions(titleIdOption, secretKeyOption, outputOption);

        _rootCommand = new RootCommand("test");
        _rootCommand.Add(titleIdOption);
        _rootCommand.Add(secretKeyOption);
        _rootCommand.Add(outputOption);
        _rootCommand.Add(ConfigCommands.CreateGroup(_globalOptions));
    }

    [Fact]
    public void RootCommand_ParsesHelp()
    {
        var result = _rootCommand.Parse("--help");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ConfigShow_ParsesWithoutArgs()
    {
        var result = _rootCommand.Parse("config show");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ConfigSet_ParsesTitleIdAndSecretKey()
    {
        var result = _rootCommand.Parse("config set --title-id ABC --secret-key XYZ");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void GlobalOptions_PropagateToSubcommands()
    {
        var result = _rootCommand.Parse("--title-id GLOBAL config show");

        Assert.Empty(result.Errors);
        Assert.Equal("GLOBAL", result.GetValue(_globalOptions.TitleId));
    }
}
