using System.CommandLine;
using PlayFab.Cli.Commands.Migrate;
using PlayFab.Cli.Infrastructure;
using Xunit;

namespace PlayFab.Cli.Tests.Commands;

public class MigrateCommandParsingTests
{
    private readonly RootCommand _rootCommand;
    private readonly GlobalOptions _globalOptions;

    public MigrateCommandParsingTests()
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
        _rootCommand.Add(MigrateCommands.CreateGroup(_globalOptions));
    }

    [Fact]
    public void MigrateStatus_ParsesWithoutArgs()
    {
        var result = _rootCommand.Parse("migrate status");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MigrateUp_ParsesWithoutArgs()
    {
        var result = _rootCommand.Parse("migrate up");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MigrateUp_ParsesWithToOption()
    {
        var result = _rootCommand.Parse("migrate up --to 20261028120000");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MigrateDown_ParsesWithoutArgs()
    {
        var result = _rootCommand.Parse("migrate down");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MigrateDown_ParsesWithToOption()
    {
        var result = _rootCommand.Parse("migrate down --to 20261028120000");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MigrateCreate_ParsesNameArgument()
    {
        var result = _rootCommand.Parse("migrate create add_leaderboards");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void GlobalOptions_PropagateToMigrateSubcommands()
    {
        var result = _rootCommand.Parse("--title-id GLOBAL migrate status");

        Assert.Empty(result.Errors);
        Assert.Equal("GLOBAL", result.GetValue(_globalOptions.TitleId));
    }

    [Fact]
    public void MigrationsDirOption_AcceptsCustomPath()
    {
        var result = _rootCommand.Parse("migrate --migrations-dir ./custom status");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MigratePlanStatistics_ParsesWithoutArgs()
    {
        var result = _rootCommand.Parse("migrate plan statistics");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MigratePlanStatistics_ParsesStatisticsDirOption()
    {
        var result = _rootCommand.Parse("migrate plan statistics --statistics-dir custom/path");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MigratePlanStatistics_ParsesIncludeDeletesOption()
    {
        var result = _rootCommand.Parse("migrate plan statistics --include-deletes");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MigratePlanStatistics_ParsesAllowRecreateOption()
    {
        var result = _rootCommand.Parse("migrate plan statistics --allow-recreate");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MigratePlanStatistics_ParsesStatisticNameOption()
    {
        var result = _rootCommand.Parse("migrate plan statistics --statistic PlayerScore");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MigratePlanStatistics_ParsesAllFlags()
    {
        var result = _rootCommand.Parse("migrate plan statistics --include-deletes --allow-recreate --statistics-dir stats --statistic PlayerScore");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MigratePlanStatistics_InheritsMigrationsDirFromGroup()
    {
        var result = _rootCommand.Parse("migrate --migrations-dir ./custom plan statistics");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MigratePlanStatistics_InheritsGlobalOptions()
    {
        var result = _rootCommand.Parse("--title-id GLOBAL migrate plan statistics");

        Assert.Empty(result.Errors);
        Assert.Equal("GLOBAL", result.GetValue(_globalOptions.TitleId));
    }
}
