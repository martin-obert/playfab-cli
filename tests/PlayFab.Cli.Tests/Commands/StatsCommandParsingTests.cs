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
    public void StatsGet_ParsesAllOption()
    {
        var result = _rootCommand.Parse("stats get --statistic PlayerScore --all");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StatsGet_ParsesPlayerIdOption()
    {
        var result = _rootCommand.Parse("stats get --statistic PlayerScore --player-id ABC123");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StatsGet_MissingStatistic_ReturnsParseError()
    {
        var result = _rootCommand.Parse("stats get --all");
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void StatsGet_ParsesWithoutAllOrPlayerId()
    {
        var result = _rootCommand.Parse("stats get --statistic PlayerScore");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StatsDefinition_ParsesWithRequiredOption()
    {
        var result = _rootCommand.Parse("stats definition --statistic PlayerScore");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StatsDefinition_MissingStatistic_ReturnsParseError()
    {
        var result = _rootCommand.Parse("stats definition");
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void GlobalOptions_PropagateToStatsSubcommands()
    {
        var result = _rootCommand.Parse("--title-id GLOBAL stats seed --statistic PlayerScore --column Score:100-2000 --all");

        Assert.Empty(result.Errors);
        Assert.Equal("GLOBAL", result.GetValue(_globalOptions.TitleId));
    }

    [Fact]
    public void StatsPull_ParsesStatisticOption()
    {
        var result = _rootCommand.Parse("stats pull --statistic PlayerScore");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StatsPull_ParsesAllOption()
    {
        var result = _rootCommand.Parse("stats pull --all");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StatsPull_ParsesStatisticsDirOption()
    {
        var result = _rootCommand.Parse("stats pull --all --statistics-dir custom/path");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StatsPull_ParsesWithoutAnyOption()
    {
        var result = _rootCommand.Parse("stats pull");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StatsPull_StatisticsDirPropagatesFromGroup()
    {
        var result = _rootCommand.Parse("stats --statistics-dir custom/path pull --all");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StatsNuke_ParsesWithoutArgs()
    {
        var result = _rootCommand.Parse("stats nuke");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StatsNuke_GlobalOptionsPropagated()
    {
        var result = _rootCommand.Parse("--title-id GLOBAL stats nuke");

        Assert.Empty(result.Errors);
        Assert.Equal("GLOBAL", result.GetValue(_globalOptions.TitleId));
    }
}
