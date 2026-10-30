using System.CommandLine;
using PlayFab.Cli.Commands.Codegen;
using PlayFab.Cli.Infrastructure;
using Xunit;

namespace PlayFab.Cli.Tests.Commands;

public class CodegenCommandParsingTests
{
    private readonly RootCommand _rootCommand;
    private readonly GlobalOptions _globalOptions;

    public CodegenCommandParsingTests()
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
        _rootCommand.Add(CodegenCommands.CreateGroup(_globalOptions));
    }

    // --- statistics ---

    [Fact]
    public void CodegenStatistics_ParsesWithoutArgs()
    {
        var result = _rootCommand.Parse("codegen statistics");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CodegenStatistics_ParsesNameOption()
    {
        var result = _rootCommand.Parse("codegen statistics --name PlayerScore");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CodegenStatistics_ParsesGroupLevelOptions()
    {
        var result = _rootCommand.Parse(
            "codegen --statistics-dir custom/stats --output-dir custom/out --namespace My.Namespace statistics");
        Assert.Empty(result.Errors);
    }

    // --- leaderboards ---

    [Fact]
    public void CodegenLeaderboards_ParsesWithoutArgs()
    {
        var result = _rootCommand.Parse("codegen leaderboards");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CodegenLeaderboards_ParsesNameOption()
    {
        var result = _rootCommand.Parse("codegen leaderboards --name TopScores");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CodegenLeaderboards_ParsesGroupLevelOptions()
    {
        var result = _rootCommand.Parse(
            "codegen --statistics-dir custom/stats --leaderboards-dir custom/lbs --output-dir custom/out --namespace My.Namespace leaderboards");
        Assert.Empty(result.Errors);
    }

    // --- all ---

    [Fact]
    public void CodegenAll_ParsesWithoutArgs()
    {
        var result = _rootCommand.Parse("codegen all");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CodegenAll_ParsesGroupLevelOptions()
    {
        var result = _rootCommand.Parse(
            "codegen --statistics-dir custom/stats --leaderboards-dir custom/lbs --output-dir custom/out --namespace My.Namespace all");
        Assert.Empty(result.Errors);
    }

    // --- global options ---

    [Fact]
    public void GlobalOptions_PropagateToCodegenSubcommands()
    {
        var result = _rootCommand.Parse("--title-id GLOBAL codegen statistics");

        Assert.Empty(result.Errors);
        Assert.Equal("GLOBAL", result.GetValue(_globalOptions.TitleId));
    }
}
