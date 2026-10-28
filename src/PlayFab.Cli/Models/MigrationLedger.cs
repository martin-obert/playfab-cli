namespace PlayFab.Cli.Models;

public sealed class MigrationLedger
{
    public List<MigrationRecord> AppliedMigrations { get; init; } = [];
}

public sealed class MigrationRecord
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required DateTime AppliedAt { get; init; }
}
