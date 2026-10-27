using PlayFab.ProgressionModels;
using PlayFab.Cli.Commands.Leaderboard;
using Xunit;

namespace PlayFab.Cli.Tests;

public class SeedEngineTests
{
    [Fact]
    public void ValidateOptions_ValidInput_NoErrors()
    {
        Assert.Empty(SeedEngine.ValidateOptions(null, 50, 0, 10000, 100));
        Assert.Empty(SeedEngine.ValidateOptions("statistic", 1, 5, 5, 1));
        Assert.Empty(SeedEngine.ValidateOptions("leaderboard", 1, 5, 5, 1));
    }

    [Fact]
    public void ValidateOptions_InvalidType_ReturnsError()
    {
        var errors = SeedEngine.ValidateOptions("bogus", 50, 0, 10000, 100);
        Assert.Contains(errors, e => e.Contains("--type"));
    }

    [Fact]
    public void ValidateOptions_NonPositiveCount_ReturnsError()
    {
        Assert.Contains(SeedEngine.ValidateOptions(null, 0, 0, 10000, 100), e => e.Contains("--count"));
        Assert.Contains(SeedEngine.ValidateOptions(null, -1, 0, 10000, 100), e => e.Contains("--count"));
    }

    [Fact]
    public void ValidateOptions_MinGreaterThanMax_ReturnsError()
    {
        var errors = SeedEngine.ValidateOptions(null, 50, 100, 0, 100);
        Assert.Contains(errors, e => e.Contains("--min"));
    }

    [Fact]
    public void ValidateOptions_NonPositiveBatchSize_ReturnsError()
    {
        var errors = SeedEngine.ValidateOptions(null, 50, 0, 10000, 0);
        Assert.Contains(errors, e => e.Contains("--batch-size"));
    }

    [Theory]
    [InlineData(0, 50, 50)]
    [InlineData(50, 50, 0)]
    [InlineData(60, 50, 0)]
    public void CalculateShortfall_ReturnsExpected(int existing, int desired, int expected)
    {
        Assert.Equal(expected, SeedEngine.CalculateShortfall(existing, desired));
    }

    [Fact]
    public void GenerateScores_ReturnsOneValuePerColumnWithinRange()
    {
        var rng = new Random(42);
        var scores = SeedEngine.GenerateScores(3, rng, 10, 20);

        Assert.Equal(3, scores.Count);
        foreach (var score in scores)
        {
            var value = long.Parse(score);
            Assert.InRange(value, 10, 20);
        }
    }

    [Fact]
    public void GenerateScores_SameSeed_ProducesSameSequence()
    {
        var a = SeedEngine.GenerateScores(5, new Random(123), 0, 100000);
        var b = SeedEngine.GenerateScores(5, new Random(123), 0, 100000);

        Assert.Equal(a, b);
    }

    private static StatisticDefinition MakeStat(string name) => new() { Name = name, Columns = [new StatisticColumn { Name = "Value" }] };

    private static LeaderboardDefinition MakeStandaloneLeaderboard(string name) => new()
    {
        Name = name,
        Columns = [new LeaderboardColumn { Name = "Score" }],
    };

    private static LeaderboardDefinition MakeLinkedLeaderboard(string name, string linkedStatName) => new()
    {
        Name = name,
        Columns =
        [
            new LeaderboardColumn
            {
                Name = "Score",
                LinkedStatisticColumn = new LinkedStatisticColumn { LinkedStatisticName = linkedStatName, LinkedStatisticColumnName = "Value" },
            },
        ],
    };

    [Fact]
    public void ResolveTargets_NoName_ReturnsAllStatsAndSplitsLeaderboards()
    {
        var stats = new List<StatisticDefinition> { MakeStat("StatA") };
        var leaderboards = new List<LeaderboardDefinition>
        {
            MakeStandaloneLeaderboard("Standalone"),
            MakeLinkedLeaderboard("Linked", "StatA"),
        };

        var (resolution, error) = SeedEngine.ResolveTargets(stats, leaderboards, null, null);

        Assert.Null(error);
        Assert.NotNull(resolution);
        Assert.Equal(["StatA"], resolution!.Statistics.Select(s => s.Name));
        Assert.Equal(["Standalone"], resolution.StandaloneLeaderboards.Select(l => l.Name));
        Assert.Equal(["Linked"], resolution.LinkedLeaderboardNotes.Select(n => n.LeaderboardName));
    }

    [Fact]
    public void ResolveTargets_NameMatchesOnlyStatistic_ReturnsThatStatistic()
    {
        var stats = new List<StatisticDefinition> { MakeStat("StatA") };
        var leaderboards = new List<LeaderboardDefinition>();

        var (resolution, error) = SeedEngine.ResolveTargets(stats, leaderboards, "StatA", null);

        Assert.Null(error);
        Assert.Equal(["StatA"], resolution!.Statistics.Select(s => s.Name));
        Assert.Empty(resolution.StandaloneLeaderboards);
    }

    [Fact]
    public void ResolveTargets_NameMatchesOnlyStandaloneLeaderboard_ReturnsThatLeaderboard()
    {
        var stats = new List<StatisticDefinition>();
        var leaderboards = new List<LeaderboardDefinition> { MakeStandaloneLeaderboard("Standalone") };

        var (resolution, error) = SeedEngine.ResolveTargets(stats, leaderboards, "Standalone", null);

        Assert.Null(error);
        Assert.Equal(["Standalone"], resolution!.StandaloneLeaderboards.Select(l => l.Name));
        Assert.Empty(resolution.Statistics);
    }

    [Fact]
    public void ResolveTargets_NameMatchesLinkedLeaderboard_RedirectsToUnderlyingStatistic()
    {
        var stats = new List<StatisticDefinition> { MakeStat("StatA") };
        var leaderboards = new List<LeaderboardDefinition> { MakeLinkedLeaderboard("Linked", "StatA") };

        var (resolution, error) = SeedEngine.ResolveTargets(stats, leaderboards, "Linked", null);

        Assert.Null(error);
        Assert.Equal(["StatA"], resolution!.Statistics.Select(s => s.Name));
        Assert.Empty(resolution.StandaloneLeaderboards);
        Assert.Single(resolution.LinkedLeaderboardNotes);
        Assert.Equal("Linked", resolution.LinkedLeaderboardNotes[0].LeaderboardName);
    }

    [Fact]
    public void ResolveTargets_LinkedLeaderboardWithMissingStatistic_ReturnsError()
    {
        var stats = new List<StatisticDefinition>();
        var leaderboards = new List<LeaderboardDefinition> { MakeLinkedLeaderboard("Linked", "GoneStat") };

        var (resolution, error) = SeedEngine.ResolveTargets(stats, leaderboards, "Linked", null);

        Assert.Null(resolution);
        Assert.Contains("GoneStat", error);
    }

    [Fact]
    public void ResolveTargets_NameMatchesBothWithoutType_ReturnsAmbiguousError()
    {
        var stats = new List<StatisticDefinition> { MakeStat("Shared") };
        var leaderboards = new List<LeaderboardDefinition> { MakeStandaloneLeaderboard("Shared") };

        var (resolution, error) = SeedEngine.ResolveTargets(stats, leaderboards, "Shared", null);

        Assert.Null(resolution);
        Assert.Contains("--type", error);
    }

    [Fact]
    public void ResolveTargets_NameMatchesBothWithType_Disambiguates()
    {
        var stats = new List<StatisticDefinition> { MakeStat("Shared") };
        var leaderboards = new List<LeaderboardDefinition> { MakeStandaloneLeaderboard("Shared") };

        var (statResolution, statError) = SeedEngine.ResolveTargets(stats, leaderboards, "Shared", "statistic");
        Assert.Null(statError);
        Assert.Single(statResolution!.Statistics);
        Assert.Empty(statResolution.StandaloneLeaderboards);

        var (lbResolution, lbError) = SeedEngine.ResolveTargets(stats, leaderboards, "Shared", "leaderboard");
        Assert.Null(lbError);
        Assert.Single(lbResolution!.StandaloneLeaderboards);
        Assert.Empty(lbResolution.Statistics);
    }

    [Fact]
    public void ResolveTargets_NameNotFound_ReturnsError()
    {
        var (resolution, error) = SeedEngine.ResolveTargets([], [], "Missing", null);

        Assert.Null(resolution);
        Assert.Contains("not found", error);
    }
}
