using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;
using Xunit;

namespace PlayFab.Cli.Tests.Infrastructure;

public class CodeGeneratorTests
{
    // --- BuildStatisticClassSpec ---

    [Fact]
    public void BuildStatisticClassSpec_SingleColumn_NumberDefaultsToFloat()
    {
        var statistic = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum", Type = "number" }],
        };

        var spec = CodeGenerator.BuildStatisticClassSpec(statistic);

        Assert.Equal("PlayerScoreStatistic", spec.ClassName);
        Assert.Equal("Name", spec.ConstName);
        Assert.Equal("PlayerScore", spec.ConstValue);
        Assert.Empty(spec.MetadataProperties);
        var prop = Assert.Single(spec.ColumnProperties);
        Assert.Equal("Value", prop.PropertyName);
        Assert.Equal("float", prop.CSharpType);
        Assert.Equal("Value", prop.OriginalColumnName);
    }

    [Fact]
    public void BuildStatisticClassSpec_ExplicitInt16_MapsToShort()
    {
        var statistic = new StatisticDefinitionFile
        {
            Name = "RaceResult",
            Columns = [new StatisticColumnDefinition { Name = "Laps", AggregationMethod = "Max", Type = "int16" }],
        };

        var spec = CodeGenerator.BuildStatisticClassSpec(statistic);

        Assert.Equal("short", spec.ColumnProperties[0].CSharpType);
    }

    [Fact]
    public void BuildStatisticClassSpec_MultiColumn_PreservesOrder()
    {
        var statistic = new StatisticDefinitionFile
        {
            Name = "RaceResult",
            Columns =
            [
                new StatisticColumnDefinition { Name = "Time", AggregationMethod = "Min", Type = "float32" },
                new StatisticColumnDefinition { Name = "Distance", AggregationMethod = "Max", Type = "int32" },
                new StatisticColumnDefinition { Name = "Laps", AggregationMethod = "Sum", Type = "int16" },
            ],
        };

        var spec = CodeGenerator.BuildStatisticClassSpec(statistic);

        Assert.Equal(3, spec.ColumnProperties.Count);
        Assert.Equal("Time", spec.ColumnProperties[0].PropertyName);
        Assert.Equal("Distance", spec.ColumnProperties[1].PropertyName);
        Assert.Equal("Laps", spec.ColumnProperties[2].PropertyName);
    }

    [Fact]
    public void BuildStatisticClassSpec_SanitizesClassAndPropertyNames()
    {
        var statistic = new StatisticDefinitionFile
        {
            Name = "st.RW001.hard",
            Columns = [new StatisticColumnDefinition { Name = "Score-Value", AggregationMethod = "Max" }],
        };

        var spec = CodeGenerator.BuildStatisticClassSpec(statistic);

        Assert.Equal("stRW001hardStatistic", spec.ClassName);
        Assert.Equal("ScoreValue", spec.ColumnProperties[0].PropertyName);
    }

    // --- BuildLeaderboardClassSpec ---

    [Fact]
    public void BuildLeaderboardClassSpec_UnlinkedColumn_DefaultsToString()
    {
        var leaderboard = new LeaderboardDefinitionFile
        {
            Name = "TopScores",
            Columns = [new LeaderboardColumnDefinition { Name = "Rank", SortDirection = "Ascending" }],
        };

        var spec = CodeGenerator.BuildLeaderboardClassSpec(leaderboard, []);

        Assert.Equal("string", spec.ColumnProperties[0].CSharpType);
    }

    [Fact]
    public void BuildLeaderboardClassSpec_LinkedColumn_InheritsStatisticType()
    {
        var statistics = new List<StatisticDefinitionFile>
        {
            new()
            {
                Name = "PlayerScore",
                Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum", Type = "int32" }],
            },
        };
        var leaderboard = new LeaderboardDefinitionFile
        {
            Name = "TopScores",
            Columns = [new LeaderboardColumnDefinition { Name = "Score", SortDirection = "Descending", LinkedStatistic = "PlayerScore", LinkedStatisticColumn = "Value" }],
        };

        var spec = CodeGenerator.BuildLeaderboardClassSpec(leaderboard, statistics);

        Assert.Equal("int", spec.ColumnProperties[0].CSharpType);
    }

    [Fact]
    public void BuildLeaderboardClassSpec_LinkedColumnWithExplicitType_OverrideWins()
    {
        var statistics = new List<StatisticDefinitionFile>
        {
            new()
            {
                Name = "PlayerScore",
                Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum", Type = "int32" }],
            },
        };
        var leaderboard = new LeaderboardDefinitionFile
        {
            Name = "TopScores",
            Columns =
            [
                new LeaderboardColumnDefinition
                {
                    Name = "Score",
                    SortDirection = "Descending",
                    LinkedStatistic = "PlayerScore",
                    LinkedStatisticColumn = "Value",
                    Type = "float64",
                },
            ],
        };

        var spec = CodeGenerator.BuildLeaderboardClassSpec(leaderboard, statistics);

        Assert.Equal("double", spec.ColumnProperties[0].CSharpType);
    }

    [Fact]
    public void BuildLeaderboardClassSpec_LinkToNonexistentStatistic_FallsBackToStringWithoutThrowing()
    {
        var leaderboard = new LeaderboardDefinitionFile
        {
            Name = "TopScores",
            Columns = [new LeaderboardColumnDefinition { Name = "Score", SortDirection = "Descending", LinkedStatistic = "DoesNotExist", LinkedStatisticColumn = "Value" }],
        };

        var spec = CodeGenerator.BuildLeaderboardClassSpec(leaderboard, []);

        Assert.Equal("string", spec.ColumnProperties[0].CSharpType);
    }

    [Fact]
    public void BuildLeaderboardClassSpec_LinkToNonexistentColumnOnExistingStatistic_FallsBackToString()
    {
        var statistics = new List<StatisticDefinitionFile>
        {
            new() { Name = "PlayerScore", Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum", Type = "int32" }] },
        };
        var leaderboard = new LeaderboardDefinitionFile
        {
            Name = "TopScores",
            Columns = [new LeaderboardColumnDefinition { Name = "Score", SortDirection = "Descending", LinkedStatistic = "PlayerScore", LinkedStatisticColumn = "NoSuchColumn" }],
        };

        var spec = CodeGenerator.BuildLeaderboardClassSpec(leaderboard, statistics);

        Assert.Equal("string", spec.ColumnProperties[0].CSharpType);
    }

    [Fact]
    public void BuildLeaderboardClassSpec_ColumnNameCollidesWithMetadataProperty_IsDisambiguated()
    {
        var leaderboard = new LeaderboardDefinitionFile
        {
            Name = "TopScores",
            Columns =
            [
                new LeaderboardColumnDefinition { Name = "Score", SortDirection = "Descending" },
                new LeaderboardColumnDefinition { Name = "Rank", SortDirection = "Ascending" },
            ],
        };

        var spec = CodeGenerator.BuildLeaderboardClassSpec(leaderboard, []);

        Assert.Equal("Rank", spec.MetadataProperties[0].PropertyName);
        var scoreProp = spec.ColumnProperties.Single(p => p.OriginalColumnName == "Score");
        var rankColumnProp = spec.ColumnProperties.Single(p => p.OriginalColumnName == "Rank");
        Assert.Equal("Score", scoreProp.PropertyName);
        Assert.Equal("RankColumn", rankColumnProp.PropertyName);
        // No duplicate property names across metadata + columns.
        var allNames = spec.MetadataProperties.Concat(spec.ColumnProperties).Select(p => p.PropertyName).ToList();
        Assert.Equal(allNames.Count, allNames.Distinct().Count());
    }

    [Fact]
    public void BuildStatisticClassSpec_SanitizedNameCollision_IsDisambiguated()
    {
        var statistic = new StatisticDefinitionFile
        {
            Name = "RaceResult",
            Columns =
            [
                new StatisticColumnDefinition { Name = "Score.A", AggregationMethod = "Max" },
                new StatisticColumnDefinition { Name = "Score-A", AggregationMethod = "Max" },
            ],
        };

        var spec = CodeGenerator.BuildStatisticClassSpec(statistic);

        Assert.Equal("ScoreA", spec.ColumnProperties[0].PropertyName);
        Assert.Equal("ScoreAColumn", spec.ColumnProperties[1].PropertyName);
    }

    [Fact]
    public void BuildLeaderboardClassSpec_AlwaysIncludesMetadataProperties()
    {
        var leaderboard = new LeaderboardDefinitionFile
        {
            Name = "TopScores",
            Columns = [new LeaderboardColumnDefinition { Name = "Score", SortDirection = "Descending" }],
        };

        var spec = CodeGenerator.BuildLeaderboardClassSpec(leaderboard, []);

        Assert.Equal(3, spec.MetadataProperties.Count);
        Assert.Contains(spec.MetadataProperties, p => p.PropertyName == "Rank" && p.CSharpType == "int");
        Assert.Contains(spec.MetadataProperties, p => p.PropertyName == "EntityId" && p.CSharpType == "string");
        Assert.Contains(spec.MetadataProperties, p => p.PropertyName == "DisplayName" && p.CSharpType == "string");
        Assert.Equal("TopScoresEntry", spec.ClassName);
    }

    // --- ResolveCSharpType ---

    [Theory]
    [InlineData(null, "string")]
    [InlineData("string", "string")]
    [InlineData("bool", "bool")]
    [InlineData("number", "float")]
    [InlineData("int8", "sbyte")]
    [InlineData("int16", "short")]
    [InlineData("int32", "int")]
    [InlineData("int64", "long")]
    [InlineData("uint8", "byte")]
    [InlineData("uint16", "ushort")]
    [InlineData("uint32", "uint")]
    [InlineData("uint64", "ulong")]
    [InlineData("float32", "float")]
    [InlineData("float64", "double")]
    [InlineData("garbage", "string")]
    public void ResolveCSharpType_MapsEveryType(string? localType, string expected)
    {
        Assert.Equal(expected, CodeGenerator.ResolveCSharpType(localType));
    }

    // --- SanitizeIdentifier ---

    [Theory]
    [InlineData("PlayerScore", "PlayerScore")]
    [InlineData("Top.Scores", "TopScores")]
    [InlineData("st-RW001-hard", "stRW001hard")]
    [InlineData("123Start", "_123Start")]
    [InlineData("", "Unnamed")]
    [InlineData("...", "Unnamed")]
    public void SanitizeIdentifier_ProducesValidIdentifiers(string input, string expected)
    {
        Assert.Equal(expected, CodeGenerator.SanitizeIdentifier(input));
    }

    // --- Render ---

    [Fact]
    public void Render_Statistic_ContainsExpectedShape()
    {
        var spec = CodeGenerator.BuildStatisticClassSpec(new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum", Type = "int32" }],
        });

        var source = CodeGenerator.Render(spec, "PlayFab.Generated");

        Assert.Contains("namespace PlayFab.Generated;", source);
        Assert.Contains("public sealed class PlayerScoreStatistic", source);
        Assert.Contains("public const string Name = \"PlayerScore\";", source);
        Assert.Contains("public int Value { get; set; }", source);
        Assert.Contains("public static PlayerScoreStatistic FromScores(IReadOnlyList<string> scores)", source);
        Assert.Contains("Value = int.Parse(scores[0], CultureInfo.InvariantCulture),", source);
        Assert.Contains("public List<string> ToScores()", source);
        Assert.Contains("Value.ToString(CultureInfo.InvariantCulture),", source);
    }

    [Fact]
    public void Render_StringColumn_DefaultsToEmptyAndNoParseCall()
    {
        var spec = CodeGenerator.BuildStatisticClassSpec(new StatisticDefinitionFile
        {
            Name = "PlayerName",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Last", Type = "string" }],
        });

        var source = CodeGenerator.Render(spec, "PlayFab.Generated");

        Assert.Contains("public string Value { get; set; } = \"\";", source);
        Assert.Contains("Value = scores[0],", source);
    }

    [Fact]
    public void Render_Leaderboard_IncludesMetadataAndColumnProperties()
    {
        var spec = CodeGenerator.BuildLeaderboardClassSpec(
            new LeaderboardDefinitionFile
            {
                Name = "TopScores",
                Columns = [new LeaderboardColumnDefinition { Name = "Score", SortDirection = "Descending", Type = "float32" }],
            },
            []);

        var source = CodeGenerator.Render(spec, "PlayFab.Generated");

        Assert.Contains("public sealed class TopScoresEntry", source);
        Assert.Contains("public const string Name = \"TopScores\";", source);
        Assert.Contains("public int Rank { get; set; }", source);
        Assert.Contains("public string EntityId { get; set; } = \"\";", source);
        Assert.Contains("public string DisplayName { get; set; } = \"\";", source);
        Assert.Contains("public float Score { get; set; }", source);
    }
}
