using System.CommandLine;
using Xunit;

namespace PlayFab.Cli.Tests;

public class CommandParsingTests
{
    [Fact]
    public void RootCommand_ParsesHelp()
    {
        var root = new RootCommand("test");
        var result = root.Parse("--help");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void TitleDataSet_RequiresKeyAndValue()
    {
        var keyOption = new Option<string>("--key") { Required = true };
        var valueOption = new Option<string>("--value") { Required = true };
        var command = new Command("set");
        command.Add(keyOption);
        command.Add(valueOption);

        var result = command.Parse("");
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void TitleDataSet_ParsesKeyAndValue()
    {
        var keyOption = new Option<string>("--key") { Required = true };
        var valueOption = new Option<string>("--value") { Required = true };
        var command = new Command("set");
        command.Add(keyOption);
        command.Add(valueOption);

        var result = command.Parse("--key Foo --value Bar");
        Assert.Empty(result.Errors);
        Assert.Equal("Foo", result.GetValue(keyOption));
        Assert.Equal("Bar", result.GetValue(valueOption));
    }
}
