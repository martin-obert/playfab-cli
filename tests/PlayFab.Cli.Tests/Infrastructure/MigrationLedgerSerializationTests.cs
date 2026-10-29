using System.Text.Json;
using PlayFab.Cli.Models;
using Xunit;

namespace PlayFab.Cli.Tests.Infrastructure;

public class MigrationLedgerSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void EmptyLedger_SerializesAsEmptyList()
    {
        var ledger = new MigrationLedger();
        var json = JsonSerializer.Serialize(ledger, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<MigrationLedger>(json, JsonOptions)!;

        Assert.Empty(deserialized.AppliedMigrations);
    }

    [Fact]
    public void LedgerWithRecords_RoundTrips()
    {
        var ledger = new MigrationLedger
        {
            AppliedMigrations =
            [
                new MigrationRecord
                {
                    Id = "20261028120000",
                    Name = "initial",
                    Description = "First migration",
                    AppliedAt = new DateTime(2026, 10, 28, 12, 5, 0, DateTimeKind.Utc),
                },
                new MigrationRecord
                {
                    Id = "20261028130000",
                    Name = "add_data",
                    Description = "Second migration",
                    AppliedAt = new DateTime(2026, 10, 28, 13, 5, 0, DateTimeKind.Utc),
                },
            ],
        };

        var json = JsonSerializer.Serialize(ledger, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<MigrationLedger>(json, JsonOptions)!;

        Assert.Equal(2, deserialized.AppliedMigrations.Count);
        Assert.Equal("20261028120000", deserialized.AppliedMigrations[0].Id);
        Assert.Equal("initial", deserialized.AppliedMigrations[0].Name);
        Assert.Equal("20261028130000", deserialized.AppliedMigrations[1].Id);
    }

    [Fact]
    public void DeserializeFromJson_ValidJson_Works()
    {
        var json = """{"appliedMigrations":[{"id":"20261028120000","name":"test","description":"desc","appliedAt":"2026-10-28T12:00:00"}]}""";

        var ledger = JsonSerializer.Deserialize<MigrationLedger>(json, JsonOptions)!;

        Assert.Single(ledger.AppliedMigrations);
        Assert.Equal("20261028120000", ledger.AppliedMigrations[0].Id);
    }

    [Fact]
    public void DeserializeFromJson_EmptyObject_ReturnsEmptyLedger()
    {
        var json = "{}";

        var ledger = JsonSerializer.Deserialize<MigrationLedger>(json, JsonOptions)!;

        Assert.Empty(ledger.AppliedMigrations);
    }
}
