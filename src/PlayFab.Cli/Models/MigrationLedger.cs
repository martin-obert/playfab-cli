namespace PlayFab.Cli.Models;

public sealed class MigrationLedger
{
    public List<MigrationRecord> AppliedMigrations { get; set; } = [];
}

public sealed class MigrationRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime AppliedAt { get; set; }
}
