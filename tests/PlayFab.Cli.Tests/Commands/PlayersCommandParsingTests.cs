using System.CommandLine;
using PlayFab.Cli.Commands.Players;
using PlayFab.Cli.Infrastructure;
using Xunit;

namespace PlayFab.Cli.Tests.Commands;

public class PlayersCommandParsingTests
{
    private readonly RootCommand _rootCommand;
    private readonly GlobalOptions _globalOptions;

    public PlayersCommandParsingTests()
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
        _rootCommand.Add(PlayersCommands.CreateGroup(_globalOptions));
    }

    [Fact]
    public void PlayersCreate_ParsesWithoutArgs()
    {
        var result = _rootCommand.Parse("players create");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void PlayersCreate_ParsesCountOption()
    {
        var result = _rootCommand.Parse("players create --count 5");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void PlayersDelete_ParsesAllOption()
    {
        var result = _rootCommand.Parse("players delete --all");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void PlayersDelete_ParsesPlayerIdOption()
    {
        var result = _rootCommand.Parse("players delete --player-id ABC123");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void PlayersDelete_ParsesWithoutArgs()
    {
        var result = _rootCommand.Parse("players delete");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void PlayersList_ParsesWithoutArgs()
    {
        var result = _rootCommand.Parse("players list");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void GlobalOptions_PropagateToPlayersSubcommands()
    {
        var result = _rootCommand.Parse("--title-id GLOBAL players list");

        Assert.Empty(result.Errors);
        Assert.Equal("GLOBAL", result.GetValue(_globalOptions.TitleId));
    }
}
