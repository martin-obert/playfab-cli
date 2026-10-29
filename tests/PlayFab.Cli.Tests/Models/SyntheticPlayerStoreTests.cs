using System.Text.Json;
using PlayFab.Cli.Models;
using Xunit;

namespace PlayFab.Cli.Tests.Models;

public class SyntheticPlayerStoreTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void EmptyStore_RoundTrips()
    {
        var store = new SyntheticPlayerStore { TitleId = "ABCD" };

        var json = JsonSerializer.Serialize(store, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SyntheticPlayerStore>(json, JsonOptions)!;

        Assert.Equal("ABCD", deserialized.TitleId);
        Assert.Empty(deserialized.Players);
    }

    [Fact]
    public void StoreWithPlayers_RoundTrips()
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
                    DisplayName = "TestUser1",
                    CreatedAt = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc),
                },
                new SyntheticPlayer
                {
                    PlayFabId = "PF002",
                    ServerCustomId = "synth-def",
                    DisplayName = "TestUser2",
                    CreatedAt = new DateTime(2026, 10, 29, 13, 0, 0, DateTimeKind.Utc),
                },
            ],
        };

        var json = JsonSerializer.Serialize(store, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SyntheticPlayerStore>(json, JsonOptions)!;

        Assert.Equal("ABCD", deserialized.TitleId);
        Assert.Equal(2, deserialized.Players.Count);
        Assert.Equal("PF001", deserialized.Players[0].PlayFabId);
        Assert.Equal("synth-abc", deserialized.Players[0].ServerCustomId);
        Assert.Equal("TestUser1", deserialized.Players[0].DisplayName);
        Assert.Equal("PF002", deserialized.Players[1].PlayFabId);
    }

    [Fact]
    public void Deserialization_FromRawJson()
    {
        const string json = """
            {
              "titleId": "XYZ",
              "players": [
                {
                  "playFabId": "ID1",
                  "serverCustomId": "synth-123",
                  "displayName": "CoolNinja",
                  "createdAt": "2026-10-29T14:30:00Z"
                }
              ]
            }
            """;

        var store = JsonSerializer.Deserialize<SyntheticPlayerStore>(json, JsonOptions)!;

        Assert.Equal("XYZ", store.TitleId);
        Assert.Single(store.Players);
        Assert.Equal("ID1", store.Players[0].PlayFabId);
        Assert.Equal("CoolNinja", store.Players[0].DisplayName);
    }

    [Fact]
    public void DefaultPlayers_IsEmptyList()
    {
        var store = new SyntheticPlayerStore { TitleId = "TEST" };
        Assert.NotNull(store.Players);
        Assert.Empty(store.Players);
    }
}
