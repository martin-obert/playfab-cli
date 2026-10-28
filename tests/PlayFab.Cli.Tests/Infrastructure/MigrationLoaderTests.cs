using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;
using Xunit;

namespace PlayFab.Cli.Tests.Infrastructure;

public class MigrationLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public MigrationLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"playfab-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void LoadAll_EmptyDirectory_ReturnsEmpty()
    {
        var result = MigrationLoader.LoadAll(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public void LoadAll_NonexistentDirectory_ReturnsEmpty()
    {
        var result = MigrationLoader.LoadAll(Path.Combine(_tempDir, "nonexistent"));
        Assert.Empty(result);
    }

    [Fact]
    public void LoadAll_SingleValidFile_ReturnsMigration()
    {
        WriteYaml("20261028120000_initial.yml", ValidYaml());

        var result = MigrationLoader.LoadAll(_tempDir);

        Assert.Single(result);
        Assert.Equal("20261028120000", result[0].Id);
        Assert.Equal("initial", result[0].Name);
        Assert.Equal("test migration", result[0].Definition.Description);
    }

    [Fact]
    public void LoadAll_MultipleFiles_SortedById()
    {
        WriteYaml("20261029000000_third.yml", ValidYaml());
        WriteYaml("20261028000000_first.yml", ValidYaml());
        WriteYaml("20261028120000_second.yml", ValidYaml());

        var result = MigrationLoader.LoadAll(_tempDir);

        Assert.Equal(3, result.Count);
        Assert.Equal("20261028000000", result[0].Id);
        Assert.Equal("20261028120000", result[1].Id);
        Assert.Equal("20261029000000", result[2].Id);
    }

    [Fact]
    public void LoadAll_IgnoresNonYamlFiles()
    {
        WriteYaml("20261028120000_valid.yml", ValidYaml());
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "not a migration");

        var result = MigrationLoader.LoadAll(_tempDir);

        Assert.Single(result);
    }

    [Fact]
    public void LoadAll_IgnoresMalformedFilenames()
    {
        WriteYaml("20261028120000_valid.yml", ValidYaml());
        WriteYaml("bad-name.yml", ValidYaml());
        WriteYaml("123_too_short.yml", ValidYaml());

        var result = MigrationLoader.LoadAll(_tempDir);

        Assert.Single(result);
    }

    [Fact]
    public void LoadAll_AcceptsYamlExtension()
    {
        WriteYaml("20261028120000_first.yml", ValidYaml());
        WriteYaml("20261028130000_second.yaml", ValidYaml());

        var result = MigrationLoader.LoadAll(_tempDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void LoadSingle_ParsesAllFields()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "full test"
            up:
              - type: set-title-data
                key: K1
                value: V1
              - type: delete-title-data
                key: K2
            down:
              - type: set-title-data
                key: K2
                value: restored
            """);

        var result = MigrationLoader.LoadSingle(path);

        Assert.Equal("20261028120000", result.Id);
        Assert.Equal("test", result.Name);
        Assert.Equal("full test", result.Definition.Description);
        Assert.Equal(2, result.Definition.Up.Count);
        Assert.Single(result.Definition.Down);
        Assert.Equal("V1", result.Definition.Up[0].Value);
        Assert.Null(result.Definition.Up[1].Value);
    }

    [Fact]
    public void LoadSingle_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            MigrationLoader.LoadSingle(Path.Combine(_tempDir, "20261028120000_missing.yml")));
    }

    [Fact]
    public void Validate_ValidMigration_NoErrors()
    {
        var path = WriteYaml("20261028120000_valid.yml", ValidYaml());
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_EmptyDescription_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: ""
            up:
              - type: set-title-data
                key: K
                value: V
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("Description"));
    }

    [Fact]
    public void Validate_EmptyUpSteps_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up: []
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("Up steps"));
    }

    [Fact]
    public void Validate_UnknownOperationType_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: unknown-operation
                key: K
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("Unknown operation type"));
    }

    [Fact]
    public void Validate_SetWithoutValue_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: set-title-data
                key: K
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("Value is required"));
    }

    [Fact]
    public void Validate_DeleteWithValue_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: delete-title-data
                key: K
                value: should-not-be-here
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("must not be set"));
    }

    [Fact]
    public void Validate_EmptyKey_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: set-title-data
                key: ""
                value: V
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("Key must not be empty"));
    }

    [Fact]
    public void Validate_FutureOperationType_Passes()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-statistic
                key: MyStat
                value: "max"
            down:
              - type: delete-statistic
                key: MyStat
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Empty(errors);
    }

    private string WriteYaml(string filename, string content)
    {
        var path = Path.Combine(_tempDir, filename);
        File.WriteAllText(path, content);
        return path;
    }

    private static string ValidYaml() => """
        description: "test migration"
        up:
          - type: set-title-data
            key: TestKey
            value: TestValue
        down:
          - type: delete-title-data
            key: TestKey
        """;
}
