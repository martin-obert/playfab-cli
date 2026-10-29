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
              - type: reset-statistic
                key: MyStat
                value: "max"
            down:
              - type: reset-statistic
                key: MyStat
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_BatchDeleteKeys_Valid()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "batch delete"
            up:
              - type: delete-title-data
                keys:
                  - KeyA
                  - KeyB
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_BatchSetValues_Valid()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "batch set"
            up:
              - type: set-title-data
                values:
                  GameVersion: "1.0.0"
                  MaxPlayers: "10"
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_BatchKeysWithSetType_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: set-title-data
                keys:
                  - KeyA
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("'keys' can only be used with delete"));
    }

    [Fact]
    public void Validate_BatchValuesWithDeleteType_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: delete-title-data
                values:
                  KeyA: V1
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("'values' can only be used with set"));
    }

    [Fact]
    public void Validate_AmbiguousKeyAndKeys_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: delete-title-data
                key: KeyA
                keys:
                  - KeyB
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("Only one of"));
    }

    [Fact]
    public void Validate_EmptyKeysList_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: delete-title-data
                keys: []
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("'keys' must not be empty"));
    }

    [Fact]
    public void Validate_EmptyValuesDictionary_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: set-title-data
                values: {}
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("'values' must not be empty"));
    }

    [Fact]
    public void Validate_NoKeyOrKeysOrValues_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: set-title-data
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("Must specify"));
    }

    [Fact]
    public void Validate_CreateStatisticSingleColumn_Valid()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-statistic
                name: PlayerScore
                aggregationMethod: Sum
            down:
              - type: delete-statistic
                name: PlayerScore
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_CreateStatisticMultiColumn_Valid()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-statistic
                name: RaceResult
                columns:
                  - name: Time
                    aggregationMethod: Min
                  - name: Distance
                    aggregationMethod: Max
            down:
              - type: delete-statistic
                name: RaceResult
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_CreateStatisticMissingName_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-statistic
                aggregationMethod: Sum
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("Name must not be empty"));
    }

    [Fact]
    public void Validate_CreateStatisticBothAggregationAndColumns_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-statistic
                name: PlayerScore
                aggregationMethod: Sum
                columns:
                  - name: Score
                    aggregationMethod: Sum
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("Only one of 'aggregationMethod' or 'columns'"));
    }

    [Fact]
    public void Validate_CreateStatisticNeitherAggregationNorColumns_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-statistic
                name: PlayerScore
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("Must specify either 'aggregationMethod'"));
    }

    [Fact]
    public void Validate_CreateStatisticDuplicateColumnNames_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-statistic
                name: RaceResult
                columns:
                  - name: Time
                    aggregationMethod: Min
                  - name: Time
                    aggregationMethod: Max
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("duplicate column name"));
    }

    [Fact]
    public void Validate_CreateStatisticInvalidResetInterval_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-statistic
                name: PlayerScore
                aggregationMethod: Sum
                resetInterval: Fortnight
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("Invalid resetInterval"));
    }

    [Fact]
    public void Validate_CreateStatisticInvalidMaxQueryableVersions_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-statistic
                name: PlayerScore
                aggregationMethod: Sum
                maxQueryableVersions: 0
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("maxQueryableVersions must be >= 1"));
    }

    [Fact]
    public void Validate_CreateStatisticWithStrayKey_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-statistic
                name: PlayerScore
                aggregationMethod: Sum
                key: ShouldNotBeHere
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("'key', 'keys', and 'values' cannot be used"));
    }

    [Fact]
    public void Validate_DeleteStatisticMissingName_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: delete-statistic
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("Name must not be empty"));
    }

    [Fact]
    public void Validate_DeleteStatisticWithStrayField_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: delete-statistic
                name: PlayerScore
                aggregationMethod: Sum
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("cannot be used with 'delete-statistic'"));
    }

    [Fact]
    public void Validate_CreateLeaderboard_Valid()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-leaderboard
                name: TopScores
                leaderboardColumns:
                  - name: Score
                    sortDirection: Descending
            down:
              - type: delete-leaderboard
                name: TopScores
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_CreateLeaderboardMultiColumn_Valid()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-leaderboard
                name: RaceResult
                sizeLimit: 500
                resetInterval: Week
                maxQueryableVersions: 2
                leaderboardColumns:
                  - name: Time
                    sortDirection: Ascending
                  - name: Distance
                    sortDirection: Descending
            down:
              - type: delete-leaderboard
                name: RaceResult
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_CreateLeaderboardMissingName_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-leaderboard
                leaderboardColumns:
                  - name: Score
                    sortDirection: Descending
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("Name must not be empty"));
    }

    [Fact]
    public void Validate_CreateLeaderboardEmptyColumns_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-leaderboard
                name: TopScores
                leaderboardColumns: []
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("'leaderboardColumns' must not be empty"));
    }

    [Fact]
    public void Validate_CreateLeaderboardMissingColumns_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-leaderboard
                name: TopScores
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("'leaderboardColumns' must not be empty"));
    }

    [Fact]
    public void Validate_CreateLeaderboardDuplicateColumnNames_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-leaderboard
                name: TopScores
                leaderboardColumns:
                  - name: Score
                    sortDirection: Descending
                  - name: Score
                    sortDirection: Ascending
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("duplicate leaderboard column name"));
    }

    [Fact]
    public void Validate_CreateLeaderboardInvalidSortDirection_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-leaderboard
                name: TopScores
                leaderboardColumns:
                  - name: Score
                    sortDirection: Sideways
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("invalid sortDirection"));
    }

    [Fact]
    public void Validate_CreateLeaderboardInvalidResetInterval_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-leaderboard
                name: TopScores
                resetInterval: Fortnight
                leaderboardColumns:
                  - name: Score
                    sortDirection: Descending
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("Invalid resetInterval"));
    }

    [Fact]
    public void Validate_CreateLeaderboardInvalidSizeLimit_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-leaderboard
                name: TopScores
                sizeLimit: 0
                leaderboardColumns:
                  - name: Score
                    sortDirection: Descending
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("sizeLimit must be >= 1"));
    }

    [Fact]
    public void Validate_CreateLeaderboardWithStrayKey_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-leaderboard
                name: TopScores
                key: ShouldNotBeHere
                leaderboardColumns:
                  - name: Score
                    sortDirection: Descending
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("'key', 'keys', and 'values' cannot be used"));
    }

    [Fact]
    public void Validate_DeleteLeaderboardMissingName_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: delete-leaderboard
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("Name must not be empty"));
    }

    [Fact]
    public void Validate_DeleteLeaderboardWithStrayField_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: delete-leaderboard
                name: TopScores
                sizeLimit: 1000
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("cannot be used with 'delete-leaderboard'"));
    }

    [Fact]
    public void Validate_CreateLeaderboardLinkedColumn_Valid()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-leaderboard
                name: TopScores
                leaderboardColumns:
                  - name: Score
                    sortDirection: Descending
                    linkedStatistic: PlayerScore
                    linkedStatisticColumn: Value
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_CreateLeaderboardMixedLinkedAndPlainColumns_Valid()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-leaderboard
                name: TopScores
                leaderboardColumns:
                  - name: Score
                    sortDirection: Descending
                    linkedStatistic: PlayerScore
                    linkedStatisticColumn: Value
                  - name: Rank
                    sortDirection: Ascending
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_CreateLeaderboardLinkedStatisticWithoutColumn_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-leaderboard
                name: TopScores
                leaderboardColumns:
                  - name: Score
                    sortDirection: Descending
                    linkedStatistic: PlayerScore
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("must both be set or both be omitted"));
    }

    [Fact]
    public void Validate_CreateLeaderboardLinkedColumnWithoutStatistic_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-leaderboard
                name: TopScores
                leaderboardColumns:
                  - name: Score
                    sortDirection: Descending
                    linkedStatisticColumn: Value
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("must both be set or both be omitted"));
    }

    [Fact]
    public void Validate_CreateLeaderboardEmptyLinkedStatistic_ReturnsError()
    {
        var path = WriteYaml("20261028120000_test.yml", """
            description: "test"
            up:
              - type: create-leaderboard
                name: TopScores
                leaderboardColumns:
                  - name: Score
                    sortDirection: Descending
                    linkedStatistic: ""
                    linkedStatisticColumn: Value
            down: []
            """);
        var migration = MigrationLoader.LoadSingle(path);

        var errors = MigrationLoader.Validate(migration);

        Assert.Contains(errors, e => e.Contains("linkedStatistic must not be empty"));
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
