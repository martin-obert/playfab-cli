using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;
using Xunit;

namespace PlayFab.Cli.Tests.Infrastructure;

public class LeaderboardDefinitionStoreTests : IDisposable
{
    private readonly string _tempDir;

    public LeaderboardDefinitionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"playfab_lb_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // --- LoadAll ---

    [Fact]
    public void LoadAll_EmptyDir_ReturnsEmptyList()
    {
        var result = LeaderboardDefinitionStore.LoadAll(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public void LoadAll_NonexistentDir_ReturnsEmptyList()
    {
        var result = LeaderboardDefinitionStore.LoadAll(Path.Combine(_tempDir, "nope"));
        Assert.Empty(result);
    }

    [Fact]
    public void LoadAll_MultipleFiles_ReturnsOrderedByFilename()
    {
        WriteYaml("Beta.yml", MakeYaml("Beta"));
        WriteYaml("Alpha.yml", MakeYaml("Alpha"));

        var result = LeaderboardDefinitionStore.LoadAll(_tempDir);

        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha", result[0].Name);
        Assert.Equal("Beta", result[1].Name);
    }

    [Fact]
    public void LoadAll_MixedExtensions_LoadsBoth()
    {
        WriteYaml("One.yml", MakeYaml("One"));
        WriteYaml("Two.yaml", MakeYaml("Two"));

        var result = LeaderboardDefinitionStore.LoadAll(_tempDir);

        Assert.Equal(2, result.Count);
    }

    // --- LoadSingle ---

    [Fact]
    public void LoadSingle_ValidFile_ReturnsDefinition()
    {
        var path = WriteYaml("TopScores.yml", MakeYaml("TopScores"));
        var result = LeaderboardDefinitionStore.LoadSingle(path);
        Assert.Equal("TopScores", result.Name);
    }

    [Fact]
    public void LoadSingle_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            LeaderboardDefinitionStore.LoadSingle(Path.Combine(_tempDir, "nope.yml")));
    }

    // --- Save ---

    [Fact]
    public void Save_WritesYamlFile()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "TopScores",
            Columns = [new LeaderboardColumnDefinition { Name = "Score", SortDirection = "Descending" }],
        };

        LeaderboardDefinitionStore.Save(def, _tempDir);

        var filePath = Path.Combine(_tempDir, "TopScores.yml");
        Assert.True(File.Exists(filePath));
        var content = File.ReadAllText(filePath);
        Assert.Contains("name: TopScores", content);
    }

    [Fact]
    public void Save_CreatesDirectoryIfNeeded()
    {
        var subDir = Path.Combine(_tempDir, "sub");
        var def = new LeaderboardDefinitionFile
        {
            Name = "Test",
            Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Ascending" }],
        };

        LeaderboardDefinitionStore.Save(def, subDir);

        Assert.True(File.Exists(Path.Combine(subDir, "Test.yml")));
    }

    // --- Validate ---

    [Fact]
    public void Validate_ValidDefinition_NoErrors()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "TopScores",
            Columns = [new LeaderboardColumnDefinition { Name = "Score", SortDirection = "Descending" }],
        };

        Assert.Empty(LeaderboardDefinitionStore.Validate(def));
    }

    [Fact]
    public void Validate_EmptyName_Error()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "",
            Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending" }],
        };

        var errors = LeaderboardDefinitionStore.Validate(def);
        Assert.Contains(errors, e => e.Contains("Name must not be empty"));
    }

    [Fact]
    public void Validate_InvalidResetInterval_Error()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "Test",
            ResetInterval = "Yearly",
            Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending" }],
        };

        var errors = LeaderboardDefinitionStore.Validate(def);
        Assert.Contains(errors, e => e.Contains("resetInterval"));
    }

    [Fact]
    public void Validate_InvalidSortDirection_Error()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "Test",
            Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "UpDown" }],
        };

        var errors = LeaderboardDefinitionStore.Validate(def);
        Assert.Contains(errors, e => e.Contains("sortDirection"));
    }

    [Fact]
    public void Validate_EmptyColumns_Error()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "Test",
            Columns = [],
        };

        var errors = LeaderboardDefinitionStore.Validate(def);
        Assert.Contains(errors, e => e.Contains("Columns must not be empty"));
    }

    [Fact]
    public void Validate_DuplicateColumnNames_Error()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "Test",
            Columns =
            [
                new LeaderboardColumnDefinition { Name = "Score", SortDirection = "Descending" },
                new LeaderboardColumnDefinition { Name = "score", SortDirection = "Ascending" },
            ],
        };

        var errors = LeaderboardDefinitionStore.Validate(def);
        Assert.Contains(errors, e => e.Contains("Duplicate column name"));
    }

    [Fact]
    public void Validate_SizeLimitZero_Error()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "Test",
            SizeLimit = 0,
            Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending" }],
        };

        var errors = LeaderboardDefinitionStore.Validate(def);
        Assert.Contains(errors, e => e.Contains("sizeLimit"));
    }

    [Fact]
    public void Validate_LinkedStatisticWithoutColumn_Error()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "Test",
            Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending", LinkedStatistic = "Stat" }],
        };

        var errors = LeaderboardDefinitionStore.Validate(def);
        Assert.Contains(errors, e => e.Contains("linkedStatistic") && e.Contains("linkedStatisticColumn"));
    }

    [Fact]
    public void Validate_LinkedStatisticColumnWithoutStatistic_Error()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "Test",
            Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending", LinkedStatisticColumn = "Value" }],
        };

        var errors = LeaderboardDefinitionStore.Validate(def);
        Assert.Contains(errors, e => e.Contains("linkedStatistic") && e.Contains("linkedStatisticColumn"));
    }

    [Fact]
    public void Validate_BothLinkedFieldsSet_NoError()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "Test",
            Columns = [new LeaderboardColumnDefinition
            {
                Name = "V",
                SortDirection = "Descending",
                LinkedStatistic = "PlayerScore",
                LinkedStatisticColumn = "Value",
            }],
        };

        Assert.Empty(LeaderboardDefinitionStore.Validate(def));
    }

    [Fact]
    public void Validate_MaxQueryableVersionsZero_Error()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "Test",
            MaxQueryableVersions = 0,
            Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending" }],
        };

        var errors = LeaderboardDefinitionStore.Validate(def);
        Assert.Contains(errors, e => e.Contains("maxQueryableVersions"));
    }

    [Fact]
    public void Validate_NullType_NoError()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "Test",
            Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending" }],
        };

        Assert.Empty(LeaderboardDefinitionStore.Validate(def));
    }

    [Theory]
    [InlineData("string")]
    [InlineData("number")]
    [InlineData("bool")]
    [InlineData("int32")]
    [InlineData("float32")]
    public void Validate_ValidExplicitTypes_NoError(string type)
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "Test",
            Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending", Type = type }],
        };

        Assert.Empty(LeaderboardDefinitionStore.Validate(def));
    }

    [Fact]
    public void Validate_InvalidType_Error()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "Test",
            Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending", Type = "float" }],
        };

        var errors = LeaderboardDefinitionStore.Validate(def);
        Assert.Contains(errors, e => e.Contains("invalid type"));
    }

    // --- helpers ---

    private string WriteYaml(string filename, string content)
    {
        var path = Path.Combine(_tempDir, filename);
        File.WriteAllText(path, content);
        return path;
    }

    private static string MakeYaml(string name) =>
        $"""
        name: {name}
        columns:
          - name: Score
            sortDirection: Descending
        """;
}
