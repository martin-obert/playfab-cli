using PlayFab.Cli.Infrastructure;
using PlayFab.ProgressionModels;
using Xunit;

namespace PlayFab.Cli.Tests.Infrastructure;

public class StatisticSeedServiceTests
{
    [Fact]
    public void ParseColumnSpec_ValidSpec_ReturnsNameMinMax()
    {
        var (name, min, max) = StatisticSeedService.ParseColumnSpec("Score:100-2000");

        Assert.Equal("Score", name);
        Assert.Equal(100, min);
        Assert.Equal(2000, max);
    }

    [Theory]
    [InlineData("Score")]
    [InlineData("Score:")]
    [InlineData("Score:100")]
    [InlineData("Score:abc-200")]
    [InlineData("Score:100-xyz")]
    [InlineData(":100-200")]
    public void ParseColumnSpec_MalformedSyntax_Throws(string spec)
    {
        Assert.Throws<FormatException>(() => StatisticSeedService.ParseColumnSpec(spec));
    }

    [Fact]
    public void ParseColumnSpec_MinGreaterThanMax_Throws()
    {
        var ex = Assert.Throws<FormatException>(() => StatisticSeedService.ParseColumnSpec("Score:200-100"));
        Assert.Contains("min", ex.Message);
    }

    [Fact]
    public void ValidateColumns_AllColumnsExist_ReturnsEmpty()
    {
        var errors = StatisticSeedService.ValidateColumns(["Score", "Time"], ["Score"]);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateColumns_UnknownColumn_ReturnsError()
    {
        var errors = StatisticSeedService.ValidateColumns(["Score", "Time"], ["Distance"]);

        Assert.Single(errors);
        Assert.Contains("Distance", errors[0]);
        Assert.Contains("Score", errors[0]);
        Assert.Contains("Time", errors[0]);
    }

    [Fact]
    public void ValidateColumns_CaseInsensitiveMatch_ReturnsEmpty()
    {
        var errors = StatisticSeedService.ValidateColumns(["Score"], ["score"]);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateUntouchedSumColumns_SumColumnNotTargeted_ReturnsError()
    {
        var columns = new List<StatisticColumn>
        {
            new() { Name = "Score", AggregationMethod = StatisticAggregationMethod.Sum },
            new() { Name = "Time", AggregationMethod = StatisticAggregationMethod.Min },
        };

        var errors = StatisticSeedService.ValidateUntouchedSumColumns(columns, ["Time"]);

        Assert.Single(errors);
        Assert.Contains("Score", errors[0]);
    }

    [Fact]
    public void ValidateUntouchedSumColumns_SumColumnTargeted_ReturnsEmpty()
    {
        var columns = new List<StatisticColumn>
        {
            new() { Name = "Score", AggregationMethod = StatisticAggregationMethod.Sum },
            new() { Name = "Time", AggregationMethod = StatisticAggregationMethod.Min },
        };

        var errors = StatisticSeedService.ValidateUntouchedSumColumns(columns, ["Score"]);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(StatisticAggregationMethod.Min)]
    [InlineData(StatisticAggregationMethod.Max)]
    [InlineData(StatisticAggregationMethod.Last)]
    public void ValidateUntouchedSumColumns_NonSumColumnUntargeted_ReturnsEmpty(StatisticAggregationMethod method)
    {
        var columns = new List<StatisticColumn> { new() { Name = "Time", AggregationMethod = method } };

        var errors = StatisticSeedService.ValidateUntouchedSumColumns(columns, []);

        Assert.Empty(errors);
    }

    [Fact]
    public void MergeScores_OnlyTargetedPositionsChange()
    {
        var merged = StatisticSeedService.MergeScores(
            ["Score", "Time"],
            ["50", "99"],
            new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase) { ["Score"] = (1000, 1000) });

        Assert.Equal("1000", merged[0]);
        Assert.Equal("99", merged[1]);
    }

    [Fact]
    public void MergeScores_NoPriorValue_DefaultsUntouchedPositionsToZero()
    {
        var merged = StatisticSeedService.MergeScores(
            ["Score", "Time"],
            [],
            new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase) { ["Score"] = (5, 5) });

        Assert.Equal("5", merged[0]);
        Assert.Equal("0", merged[1]);
    }

    [Fact]
    public void MergeScores_RandomValueWithinRange()
    {
        for (var i = 0; i < 50; i++)
        {
            var merged = StatisticSeedService.MergeScores(
                ["Score"],
                ["0"],
                new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase) { ["Score"] = (10, 20) });

            var value = int.Parse(merged[0]);
            Assert.InRange(value, 10, 20);
        }
    }
}
