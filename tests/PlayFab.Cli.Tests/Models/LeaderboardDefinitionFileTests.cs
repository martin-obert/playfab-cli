using PlayFab.Cli.Models;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PlayFab.Cli.Tests.Models;

public class LeaderboardDefinitionFileTests
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    [Fact]
    public void Serialize_MinimalDefinition_ProducesExpectedYaml()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "TopScores",
            Columns = [new LeaderboardColumnDefinition { Name = "Score", SortDirection = "Descending" }],
        };

        var yaml = Serializer.Serialize(def);

        Assert.Contains("name: TopScores", yaml);
        Assert.Contains("sortDirection: Descending", yaml);
    }

    [Fact]
    public void Deserialize_FullDefinition_AllFieldsMapped()
    {
        var yaml = """
            name: TopScores
            entityType: group
            resetInterval: Week
            maxQueryableVersions: 5
            sizeLimit: 500
            columns:
              - name: Score
                sortDirection: Descending
                linkedStatistic: PlayerScore
                linkedStatisticColumn: Value
              - name: Rank
                sortDirection: Ascending
            """;

        var def = Deserializer.Deserialize<LeaderboardDefinitionFile>(yaml);

        Assert.Equal("TopScores", def.Name);
        Assert.Equal("group", def.EntityType);
        Assert.Equal("Week", def.ResetInterval);
        Assert.Equal(5, def.MaxQueryableVersions);
        Assert.Equal(500, def.SizeLimit);
        Assert.Equal(2, def.Columns.Count);
        Assert.Equal("Score", def.Columns[0].Name);
        Assert.Equal("Descending", def.Columns[0].SortDirection);
        Assert.Equal("PlayerScore", def.Columns[0].LinkedStatistic);
        Assert.Equal("Value", def.Columns[0].LinkedStatisticColumn);
        Assert.Equal("Rank", def.Columns[1].Name);
        Assert.Equal("Ascending", def.Columns[1].SortDirection);
        Assert.Null(def.Columns[1].LinkedStatistic);
        Assert.Null(def.Columns[1].LinkedStatisticColumn);
    }

    [Fact]
    public void Defaults_AppliedCorrectly()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "Test",
            Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending" }],
        };

        Assert.Equal("title_player_account", def.EntityType);
        Assert.Equal("Manual", def.ResetInterval);
        Assert.Equal(1, def.MaxQueryableVersions);
        Assert.Equal(1000, def.SizeLimit);
    }

    [Fact]
    public void Roundtrip_PreservesAllFields()
    {
        var original = new LeaderboardDefinitionFile
        {
            Name = "RaceResults",
            EntityType = "group",
            ResetInterval = "Month",
            MaxQueryableVersions = 3,
            SizeLimit = 2000,
            Columns =
            [
                new LeaderboardColumnDefinition
                {
                    Name = "Time",
                    SortDirection = "Ascending",
                    LinkedStatistic = "RaceTimes",
                    LinkedStatisticColumn = "BestTime",
                },
                new LeaderboardColumnDefinition { Name = "Laps", SortDirection = "Descending" },
            ],
        };

        var yaml = Serializer.Serialize(original);
        var deserialized = Deserializer.Deserialize<LeaderboardDefinitionFile>(yaml);

        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.EntityType, deserialized.EntityType);
        Assert.Equal(original.ResetInterval, deserialized.ResetInterval);
        Assert.Equal(original.MaxQueryableVersions, deserialized.MaxQueryableVersions);
        Assert.Equal(original.SizeLimit, deserialized.SizeLimit);
        Assert.Equal(original.Columns.Count, deserialized.Columns.Count);
        Assert.Equal(original.Columns[0].LinkedStatistic, deserialized.Columns[0].LinkedStatistic);
        Assert.Equal(original.Columns[0].LinkedStatisticColumn, deserialized.Columns[0].LinkedStatisticColumn);
        Assert.Null(deserialized.Columns[1].LinkedStatistic);
    }

    [Fact]
    public void Serialize_OmitsNullLinkedStatistic()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "Test",
            Columns = [new LeaderboardColumnDefinition { Name = "Score", SortDirection = "Descending" }],
        };

        var yaml = Serializer.Serialize(def);

        Assert.DoesNotContain("linkedStatistic", yaml);
        Assert.DoesNotContain("linkedStatisticColumn", yaml);
    }
}
