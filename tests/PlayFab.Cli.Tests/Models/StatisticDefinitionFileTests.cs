using PlayFab.Cli.Models;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PlayFab.Cli.Tests.Models;

public class StatisticDefinitionFileTests
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    [Fact]
    public void Deserialize_FullDefinition_Works()
    {
        var yaml = """
            name: PlayerScore
            entityType: title_player_account
            resetInterval: Month
            maxQueryableVersions: 3
            columns:
              - name: Value
                aggregationMethod: Sum
            """;

        var def = Deserializer.Deserialize<StatisticDefinitionFile>(yaml);

        Assert.Equal("PlayerScore", def.Name);
        Assert.Equal("title_player_account", def.EntityType);
        Assert.Equal("Month", def.ResetInterval);
        Assert.Equal(3, def.MaxQueryableVersions);
        Assert.Single(def.Columns);
        Assert.Equal("Value", def.Columns[0].Name);
        Assert.Equal("Sum", def.Columns[0].AggregationMethod);
    }

    [Fact]
    public void Deserialize_MultipleColumns_PreservesOrder()
    {
        var yaml = """
            name: RaceResult
            columns:
              - name: Time
                aggregationMethod: Min
              - name: Distance
                aggregationMethod: Max
            """;

        var def = Deserializer.Deserialize<StatisticDefinitionFile>(yaml);

        Assert.Equal(2, def.Columns.Count);
        Assert.Equal("Time", def.Columns[0].Name);
        Assert.Equal("Distance", def.Columns[1].Name);
    }

    [Fact]
    public void Deserialize_DefaultValues_Applied()
    {
        var yaml = """
            name: SimpleStat
            columns:
              - name: Value
                aggregationMethod: Last
            """;

        var def = Deserializer.Deserialize<StatisticDefinitionFile>(yaml);

        Assert.Equal("title_player_account", def.EntityType);
        Assert.Equal("Manual", def.ResetInterval);
        Assert.Equal(1, def.MaxQueryableVersions);
    }

    [Fact]
    public void Deserialize_ColumnTypeExplicit_Works()
    {
        var yaml = """
            name: PlayerScore
            columns:
              - name: Score
                aggregationMethod: Sum
                type: number
              - name: Active
                aggregationMethod: Last
                type: bool
            """;

        var def = Deserializer.Deserialize<StatisticDefinitionFile>(yaml);

        Assert.Equal("number", def.Columns[0].Type);
        Assert.Equal("bool", def.Columns[1].Type);
    }

    [Fact]
    public void Deserialize_ColumnTypeOmitted_DefaultsToString()
    {
        var yaml = """
            name: PlayerScore
            columns:
              - name: Value
                aggregationMethod: Sum
            """;

        var def = Deserializer.Deserialize<StatisticDefinitionFile>(yaml);

        Assert.Equal("string", def.Columns[0].Type);
    }

    [Fact]
    public void Deserialize_ExtraFields_Ignored()
    {
        var yaml = """
            name: PlayerScore
            unknownField: 42
            columns:
              - name: Value
                aggregationMethod: Sum
                extraField: true
            """;

        var def = Deserializer.Deserialize<StatisticDefinitionFile>(yaml);

        Assert.Equal("PlayerScore", def.Name);
        Assert.Single(def.Columns);
    }

    [Fact]
    public void SerializeDeserialize_Roundtrip_PreservesAllFields()
    {
        var original = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            EntityType = "title_player_account",
            ResetInterval = "Month",
            MaxQueryableVersions = 3,
            Columns =
            [
                new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" },
                new StatisticColumnDefinition { Name = "Bonus", AggregationMethod = "Last" },
            ],
        };

        var yaml = Serializer.Serialize(original);
        var deserialized = Deserializer.Deserialize<StatisticDefinitionFile>(yaml);

        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.EntityType, deserialized.EntityType);
        Assert.Equal(original.ResetInterval, deserialized.ResetInterval);
        Assert.Equal(original.MaxQueryableVersions, deserialized.MaxQueryableVersions);
        Assert.Equal(original.Columns.Count, deserialized.Columns.Count);
        for (var i = 0; i < original.Columns.Count; i++)
        {
            Assert.Equal(original.Columns[i].Name, deserialized.Columns[i].Name);
            Assert.Equal(original.Columns[i].AggregationMethod, deserialized.Columns[i].AggregationMethod);
        }
    }
}
