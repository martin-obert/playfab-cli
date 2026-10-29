using System.CommandLine;
using PlayFab.Cli.Commands.Leaderboard;
using PlayFab.Cli.Infrastructure;
using Xunit;

namespace PlayFab.Cli.Tests.Commands;

public class LeaderboardCommandParsingTests
{
    private readonly RootCommand _rootCommand;
    private readonly GlobalOptions _globalOptions;

    public LeaderboardCommandParsingTests()
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
        _rootCommand.Add(LeaderboardCommands.CreateGroup(_globalOptions));
    }

    [Fact]
    public void LeaderboardGet_ParsesWithRequiredOption()
    {
        var result = _rootCommand.Parse("leaderboard get --name TopScores");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void LeaderboardGet_MissingName_ReturnsParseError()
    {
        var result = _rootCommand.Parse("leaderboard get");
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void LeaderboardGet_ParsesPageSizeOption()
    {
        var result = _rootCommand.Parse("leaderboard get --name TopScores --page-size 10");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void LeaderboardGet_ParsesStartingPositionOption()
    {
        var result = _rootCommand.Parse("leaderboard get --name TopScores --starting-position 50");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void LeaderboardGet_ParsesVersionOption()
    {
        var result = _rootCommand.Parse("leaderboard get --name TopScores --version 2");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void LeaderboardDefinition_ParsesWithRequiredOption()
    {
        var result = _rootCommand.Parse("leaderboard definition --name TopScores");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void LeaderboardDefinition_MissingName_ReturnsParseError()
    {
        var result = _rootCommand.Parse("leaderboard definition");
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void GlobalOptions_PropagateToLeaderboardSubcommands()
    {
        var result = _rootCommand.Parse("--title-id GLOBAL leaderboard get --name TopScores");

        Assert.Empty(result.Errors);
        Assert.Equal("GLOBAL", result.GetValue(_globalOptions.TitleId));
    }
}
