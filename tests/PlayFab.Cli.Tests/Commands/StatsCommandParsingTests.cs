using System.CommandLine;
using PlayFab.Cli.Commands.Stats;
using PlayFab.Cli.Infrastructure;
using Xunit;

namespace PlayFab.Cli.Tests.Commands;

public class StatsCommandParsingTests
{
    private readonly RootCommand _rootCommand;
    private readonly GlobalOptions _globalOptions;

    public StatsCommandParsingTests()
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
        _rootCommand.Add(StatsCommands.CreateGroup(_globalOptions));
    }

    [Fact]
    public void StatsSeed_ParsesRequiredOptions()
    {
        var result = _rootCommand.Parse("stats seed --statistic PlayerScore --column Score:100-2000 --all");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StatsSeed_MissingStatistic_ReturnsParseError()
    {
        var result = _rootCommand.Parse("stats seed --column Score:100-2000 --all");
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void StatsSeed_ParsesRepeatedColumnOption()
    {
        var result = _rootCommand.Parse("stats seed --statistic PlayerScore --column Score:100-2000 --column Time:1-60 --all");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StatsSeed_ParsesPlayerIdOption()
    {
        var result = _rootCommand.Parse("stats seed --statistic PlayerScore --column Score:100-2000 --player-id ABC123");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StatsSeed_ParsesRepeatedPlayerIdOption()
    {
        var result = _rootCommand.Parse("stats seed --statistic PlayerScore --column Score:100-2000 --player-id ABC123 --player-id DEF456");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StatsSeed_ParsesCountOption()
    {
        var result = _rootCommand.Parse("stats seed --statistic PlayerScore --column Score:100-2000 --all --count 5");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StatsSeed_ParsesWithoutAllOrPlayerId()
    {
        var result = _rootCommand.Parse("stats seed --statistic PlayerScore --column Score:100-2000");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void GlobalOptions_PropagateToStatsSubcommands()
    {
        var result = _rootCommand.Parse("--title-id GLOBAL stats seed --statistic PlayerScore --column Score:100-2000 --all");

        Assert.Empty(result.Errors);
        Assert.Equal("GLOBAL", result.GetValue(_globalOptions.TitleId));
    }
}
