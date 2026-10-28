using PlayFab.Cli.Models;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PlayFab.Cli.Tests.Models;

public class MigrationDefinitionTests
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    [Fact]
    public void Deserialize_MinimalValid_Works()
    {
        var yaml = """
            description: "test migration"
            up:
              - type: set-title-data
                key: K1
                value: V1
            down:
              - type: delete-title-data
                key: K1
            """;

        var def = Deserializer.Deserialize<MigrationDefinition>(yaml);

        Assert.Equal("test migration", def.Description);
        Assert.Single(def.Up);
        Assert.Single(def.Down);
    }

    [Fact]
    public void Deserialize_MultipleSteps_PreservesOrder()
    {
        var yaml = """
            description: "multi-step"
            up:
              - type: set-title-data
                key: First
                value: "1"
              - type: set-title-data
                key: Second
                value: "2"
              - type: set-title-data
                key: Third
                value: "3"
            down: []
            """;

        var def = Deserializer.Deserialize<MigrationDefinition>(yaml);

        Assert.Equal(3, def.Up.Count);
        Assert.Equal("First", def.Up[0].Key);
        Assert.Equal("Second", def.Up[1].Key);
        Assert.Equal("Third", def.Up[2].Key);
    }

    [Fact]
    public void Deserialize_SetTitleData_HasValue()
    {
        var yaml = """
            description: "test"
            up:
              - type: set-title-data
                key: MyKey
                value: MyValue
            down: []
            """;

        var def = Deserializer.Deserialize<MigrationDefinition>(yaml);

        Assert.Equal("set-title-data", def.Up[0].Type);
        Assert.Equal("MyKey", def.Up[0].Key);
        Assert.Equal("MyValue", def.Up[0].Value);
    }

    [Fact]
    public void Deserialize_DeleteTitleData_NullValue()
    {
        var yaml = """
            description: "test"
            up:
              - type: delete-title-data
                key: MyKey
            down: []
            """;

        var def = Deserializer.Deserialize<MigrationDefinition>(yaml);

        Assert.Equal("delete-title-data", def.Up[0].Type);
        Assert.Null(def.Up[0].Value);
    }

    [Fact]
    public void Deserialize_ExtraYamlFields_Ignored()
    {
        var yaml = """
            description: "test"
            author: "someone"
            unknownField: 42
            up:
              - type: set-title-data
                key: K
                value: V
                extraStepField: true
            down: []
            """;

        var def = Deserializer.Deserialize<MigrationDefinition>(yaml);

        Assert.Equal("test", def.Description);
        Assert.Single(def.Up);
    }
}
