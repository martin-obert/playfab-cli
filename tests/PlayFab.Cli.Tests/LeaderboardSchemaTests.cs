using PlayFab.Cli.Models;
using Xunit;

namespace PlayFab.Cli.Tests;

public class LeaderboardSchemaTests
{
    [Fact]
    public void Validate_ValidSchema_NoErrors()
    {
        var schema = new LeaderboardSchema
        {
            Statistics =
            [
                new StatisticConfig { Name = "Score", AggregationMethod = AggregationMethod.Max, ResetInterval = ResetInterval.Never },
                new StatisticConfig { Name = "Wins", AggregationMethod = AggregationMethod.Sum, ResetInterval = ResetInterval.Week },
            ]
        };

        Assert.Empty(schema.Validate());
    }

    [Fact]
    public void Validate_EmptyStatistics_ReturnsError()
    {
        var schema = new LeaderboardSchema { Statistics = [] };
        var errors = schema.Validate();
        Assert.Single(errors);
        Assert.Contains("no statistics", errors[0]);
    }

    [Fact]
    public void Validate_DuplicateNames_ReturnsError()
    {
        var schema = new LeaderboardSchema
        {
            Statistics =
            [
                new StatisticConfig { Name = "Score" },
                new StatisticConfig { Name = "score" },
            ]
        };

        var errors = schema.Validate();
        Assert.Single(errors);
        Assert.Contains("Duplicate", errors[0]);
    }

    [Fact]
    public void Validate_EmptyName_ReturnsError()
    {
        var schema = new LeaderboardSchema
        {
            Statistics = [new StatisticConfig { Name = "" }]
        };

        var errors = schema.Validate();
        Assert.Single(errors);
        Assert.Contains("empty", errors[0]);
    }

    [Fact]
    public void LoadFromFile_ValidJson_Deserializes()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, """
            {
              "statistics": [
                { "name": "Test", "aggregationMethod": "Max", "resetInterval": "Week" }
              ]
            }
            """);

            var schema = LeaderboardSchema.LoadFromFile(tmpFile);
            Assert.Single(schema.Statistics);
            Assert.Equal("Test", schema.Statistics[0].Name);
            Assert.Equal(AggregationMethod.Max, schema.Statistics[0].AggregationMethod);
            Assert.Equal(ResetInterval.Week, schema.Statistics[0].ResetInterval);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }
}
