using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;
using Xunit;

namespace PlayFab.Cli.Tests.Infrastructure;

public class SyntheticPlayerServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SyntheticPlayerServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"playfab-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void GetFilePath_IncludesTitleId()
    {
        var path = SyntheticPlayerService.GetFilePath("abcd", _tempDir);
        Assert.EndsWith("synthetic-players-ABCD.json", path);
    }

    [Fact]
    public void GetFilePath_UppercasesTitleId()
    {
        var path = SyntheticPlayerService.GetFilePath("myTitle", _tempDir);
        Assert.Contains("MYTITLE", path);
    }

    [Fact]
    public void Load_ReturnsEmptyStore_WhenFileDoesNotExist()
    {
        var store = SyntheticPlayerService.Load("ABCD", _tempDir);

        Assert.Equal("ABCD", store.TitleId);
        Assert.Empty(store.Players);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new SyntheticPlayerStore
        {
            TitleId = "ABCD",
            Players =
            [
                new SyntheticPlayer
                {
                    PlayFabId = "PF001",
                    ServerCustomId = "synth-abc",
                    DisplayName = "TestUser",
                    CreatedAt = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc),
                },
            ],
        };

        SyntheticPlayerService.Save(store, _tempDir);
        var loaded = SyntheticPlayerService.Load("ABCD", _tempDir);

        Assert.Equal("ABCD", loaded.TitleId);
        Assert.Single(loaded.Players);
        Assert.Equal("PF001", loaded.Players[0].PlayFabId);
        Assert.Equal("synth-abc", loaded.Players[0].ServerCustomId);
        Assert.Equal("TestUser", loaded.Players[0].DisplayName);
    }

    [Fact]
    public void Save_CreatesDirectoryIfNeeded()
    {
        var nestedDir = Path.Combine(_tempDir, "nested", "dir");
        var store = new SyntheticPlayerStore { TitleId = "TEST" };

        SyntheticPlayerService.Save(store, nestedDir);

        Assert.True(File.Exists(SyntheticPlayerService.GetFilePath("TEST", nestedDir)));
    }

    [Fact]
    public void GenerateDisplayName_ReturnsNonEmptyString()
    {
        var name = SyntheticPlayerService.GenerateDisplayName();
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void GenerateDisplayName_RespectsMaxLength()
    {
        for (var i = 0; i < 100; i++)
        {
            var name = SyntheticPlayerService.GenerateDisplayName();
            Assert.True(name.Length <= 25, $"Generated name '{name}' exceeds 25 chars");
        }
    }

    [Fact]
    public void GenerateDisplayName_ProducesDistinctNames()
    {
        var names = Enumerable.Range(0, 20)
            .Select(_ => SyntheticPlayerService.GenerateDisplayName())
            .ToHashSet();

        Assert.True(names.Count > 1, "Expected distinct display names across 20 generations");
    }
}
