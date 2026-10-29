using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;
using PlayFab.ProgressionModels;
using Xunit;

namespace PlayFab.Cli.Tests.Infrastructure;

public class StatisticDefinitionStoreTests : IDisposable
{
    private readonly string _tempDir;

    public StatisticDefinitionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"playfab-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // --- LoadAll ---

    [Fact]
    public void LoadAll_EmptyDirectory_ReturnsEmpty()
    {
        var result = StatisticDefinitionStore.LoadAll(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public void LoadAll_NonexistentDirectory_ReturnsEmpty()
    {
        var result = StatisticDefinitionStore.LoadAll(Path.Combine(_tempDir, "nonexistent"));
        Assert.Empty(result);
    }

    [Fact]
    public void LoadAll_SingleFile_ReturnsDefinition()
    {
        WriteYaml("PlayerScore.yml", ValidYaml("PlayerScore"));

        var result = StatisticDefinitionStore.LoadAll(_tempDir);

        Assert.Single(result);
        Assert.Equal("PlayerScore", result[0].Name);
    }

    [Fact]
    public void LoadAll_MultipleFiles_SortedByName()
    {
        WriteYaml("Zebra.yml", ValidYaml("Zebra"));
        WriteYaml("Alpha.yml", ValidYaml("Alpha"));
        WriteYaml("Middle.yml", ValidYaml("Middle"));

        var result = StatisticDefinitionStore.LoadAll(_tempDir);

        Assert.Equal(3, result.Count);
        Assert.Equal("Alpha", result[0].Name);
        Assert.Equal("Middle", result[1].Name);
        Assert.Equal("Zebra", result[2].Name);
    }

    [Fact]
    public void LoadAll_AcceptsBothExtensions()
    {
        WriteYaml("First.yml", ValidYaml("First"));
        WriteYaml("Second.yaml", ValidYaml("Second"));

        var result = StatisticDefinitionStore.LoadAll(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void LoadAll_IgnoresNonYamlFiles()
    {
        WriteYaml("PlayerScore.yml", ValidYaml("PlayerScore"));
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "not a definition");

        var result = StatisticDefinitionStore.LoadAll(_tempDir);

        Assert.Single(result);
    }

    // --- LoadSingle ---

    [Fact]
    public void LoadSingle_ValidFile_ReturnsDefinition()
    {
        var path = WriteYaml("PlayerScore.yml", ValidYaml("PlayerScore"));

        var result = StatisticDefinitionStore.LoadSingle(path);

        Assert.Equal("PlayerScore", result.Name);
        Assert.Equal("title_player_account", result.EntityType);
        Assert.Single(result.Columns);
    }

    [Fact]
    public void LoadSingle_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            StatisticDefinitionStore.LoadSingle(Path.Combine(_tempDir, "missing.yml")));
    }

    [Fact]
    public void LoadSingle_AllFieldsPopulated()
    {
        var path = WriteYaml("RaceResult.yml", """
            name: RaceResult
            entityType: title_player_account
            resetInterval: Week
            maxQueryableVersions: 5
            columns:
              - name: Time
                aggregationMethod: Min
              - name: Distance
                aggregationMethod: Max
            """);

        var result = StatisticDefinitionStore.LoadSingle(path);

        Assert.Equal("RaceResult", result.Name);
        Assert.Equal("Week", result.ResetInterval);
        Assert.Equal(5, result.MaxQueryableVersions);
        Assert.Equal(2, result.Columns.Count);
        Assert.Equal("Min", result.Columns[0].AggregationMethod);
        Assert.Equal("Max", result.Columns[1].AggregationMethod);
    }

    // --- Save ---

    [Fact]
    public void Save_CreatesFileWithCorrectName()
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" }],
        };

        StatisticDefinitionStore.Save(definition, _tempDir);

        Assert.True(File.Exists(Path.Combine(_tempDir, "PlayerScore.yml")));
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        var subDir = Path.Combine(_tempDir, "nested", "statistics");
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" }],
        };

        StatisticDefinitionStore.Save(definition, subDir);

        Assert.True(File.Exists(Path.Combine(subDir, "PlayerScore.yml")));
    }

    [Fact]
    public void Save_ThenLoad_Roundtrips()
    {
        var original = new StatisticDefinitionFile
        {
            Name = "RaceResult",
            EntityType = "title_player_account",
            ResetInterval = "Month",
            MaxQueryableVersions = 3,
            Columns =
            [
                new StatisticColumnDefinition { Name = "Time", AggregationMethod = "Min" },
                new StatisticColumnDefinition { Name = "Distance", AggregationMethod = "Max" },
            ],
        };

        StatisticDefinitionStore.Save(original, _tempDir);
        var loaded = StatisticDefinitionStore.LoadSingle(Path.Combine(_tempDir, "RaceResult.yml"));

        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.EntityType, loaded.EntityType);
        Assert.Equal(original.ResetInterval, loaded.ResetInterval);
        Assert.Equal(original.MaxQueryableVersions, loaded.MaxQueryableVersions);
        Assert.Equal(original.Columns.Count, loaded.Columns.Count);
        for (var i = 0; i < original.Columns.Count; i++)
        {
            Assert.Equal(original.Columns[i].Name, loaded.Columns[i].Name);
            Assert.Equal(original.Columns[i].AggregationMethod, loaded.Columns[i].AggregationMethod);
        }
    }

    // --- Validate ---

    [Fact]
    public void Validate_ValidDefinition_NoErrors()
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" }],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ValidMultiColumn_NoErrors()
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "RaceResult",
            ResetInterval = "Week",
            MaxQueryableVersions = 3,
            Columns =
            [
                new StatisticColumnDefinition { Name = "Time", AggregationMethod = "Min" },
                new StatisticColumnDefinition { Name = "Distance", AggregationMethod = "Max" },
            ],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_EmptyName_ReturnsError()
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" }],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Contains(errors, e => e.Contains("Name must not be empty"));
    }

    [Fact]
    public void Validate_EmptyEntityType_ReturnsError()
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            EntityType = "",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" }],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Contains(errors, e => e.Contains("EntityType must not be empty"));
    }

    [Fact]
    public void Validate_InvalidResetInterval_ReturnsError()
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            ResetInterval = "Fortnight",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" }],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Contains(errors, e => e.Contains("Invalid resetInterval"));
    }

    [Fact]
    public void Validate_InvalidMaxQueryableVersions_ReturnsError()
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            MaxQueryableVersions = 0,
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" }],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Contains(errors, e => e.Contains("maxQueryableVersions must be >= 1"));
    }

    [Fact]
    public void Validate_EmptyColumns_ReturnsError()
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            Columns = [],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Contains(errors, e => e.Contains("Columns must not be empty"));
    }

    [Fact]
    public void Validate_DuplicateColumnNames_ReturnsError()
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            Columns =
            [
                new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" },
                new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Max" },
            ],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Contains(errors, e => e.Contains("Duplicate column name"));
    }

    [Fact]
    public void Validate_DuplicateColumnNamesCaseInsensitive_ReturnsError()
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            Columns =
            [
                new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" },
                new StatisticColumnDefinition { Name = "value", AggregationMethod = "Max" },
            ],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Contains(errors, e => e.Contains("Duplicate column name"));
    }

    [Fact]
    public void Validate_EmptyColumnName_ReturnsError()
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            Columns = [new StatisticColumnDefinition { Name = "", AggregationMethod = "Sum" }],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Contains(errors, e => e.Contains("name must not be empty"));
    }

    [Fact]
    public void Validate_InvalidAggregationMethod_ReturnsError()
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Average" }],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Contains(errors, e => e.Contains("invalid aggregationMethod"));
    }

    [Fact]
    public void Validate_AggregationMethodCaseInsensitive_NoErrors()
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "sum" }],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("Manual")]
    [InlineData("Hour")]
    [InlineData("Day")]
    [InlineData("Week")]
    [InlineData("Month")]
    public void Validate_AllValidResetIntervals_NoErrors(string interval)
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            ResetInterval = interval,
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" }],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_InvalidColumnType_ReturnsError()
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum", Type = "float" }],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Contains(errors, e => e.Contains("invalid type"));
    }

    [Theory]
    [InlineData("string")]
    [InlineData("number")]
    [InlineData("bool")]
    public void Validate_AllValidColumnTypes_NoErrors(string type)
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum", Type = type }],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ColumnTypeDefaultsToString_NoErrors()
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" }],
        };

        Assert.Equal("string", definition.Columns[0].Type);

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("Last")]
    [InlineData("Min")]
    [InlineData("Max")]
    [InlineData("Sum")]
    public void Validate_AllValidAggregationMethods_NoErrors(string method)
    {
        var definition = new StatisticDefinitionFile
        {
            Name = "PlayerScore",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = method }],
        };

        var errors = StatisticDefinitionStore.Validate(definition);

        Assert.Empty(errors);
    }

    // --- MapFromPlayFab ---

    [Fact]
    public void MapFromPlayFab_MapsAllFields()
    {
        var response = new GetStatisticDefinitionResponse
        {
            Name = "PlayerScore",
            EntityType = "title_player_account",
            Columns =
            [
                new StatisticColumn { Name = "Value", AggregationMethod = StatisticAggregationMethod.Sum },
                new StatisticColumn { Name = "Bonus", AggregationMethod = StatisticAggregationMethod.Last },
            ],
            VersionConfiguration = new VersionConfiguration
            {
                ResetInterval = PlayFab.ProgressionModels.ResetInterval.Month,
                MaxQueryableVersions = 3,
            },
        };

        var result = StatisticDefinitionStore.MapFromPlayFab(response);

        Assert.Equal("PlayerScore", result.Name);
        Assert.Equal("title_player_account", result.EntityType);
        Assert.Equal("Month", result.ResetInterval);
        Assert.Equal(3, result.MaxQueryableVersions);
        Assert.Equal(2, result.Columns.Count);
        Assert.Equal("Value", result.Columns[0].Name);
        Assert.Equal("Sum", result.Columns[0].AggregationMethod);
        Assert.Equal("Bonus", result.Columns[1].Name);
        Assert.Equal("Last", result.Columns[1].AggregationMethod);
    }

    [Fact]
    public void MapFromPlayFab_DefaultsWhenFieldsNull()
    {
        var response = new GetStatisticDefinitionResponse
        {
            Name = "SimpleStat",
            EntityType = null,
            Columns = null,
            VersionConfiguration = null,
        };

        var result = StatisticDefinitionStore.MapFromPlayFab(response);

        Assert.Equal("title_player_account", result.EntityType);
        Assert.Equal("Manual", result.ResetInterval);
        Assert.Equal(1, result.MaxQueryableVersions);
        Assert.Empty(result.Columns);
    }

    [Fact]
    public void MapFromPlayFab_ResultValidates()
    {
        var response = new GetStatisticDefinitionResponse
        {
            Name = "PlayerScore",
            EntityType = "title_player_account",
            Columns = [new StatisticColumn { Name = "Value", AggregationMethod = StatisticAggregationMethod.Sum }],
            VersionConfiguration = new VersionConfiguration
            {
                ResetInterval = PlayFab.ProgressionModels.ResetInterval.Manual,
                MaxQueryableVersions = 1,
            },
        };

        var mapped = StatisticDefinitionStore.MapFromPlayFab(response);
        var errors = StatisticDefinitionStore.Validate(mapped);

        Assert.Empty(errors);
    }

    // --- helpers ---

    private string WriteYaml(string filename, string content)
    {
        var path = Path.Combine(_tempDir, filename);
        File.WriteAllText(path, content);
        return path;
    }

    private static string ValidYaml(string name) => $"""
        name: {name}
        entityType: title_player_account
        resetInterval: Manual
        maxQueryableVersions: 1
        columns:
          - name: Value
            aggregationMethod: Sum
        """;
}
