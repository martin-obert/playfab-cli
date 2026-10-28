namespace PlayFab.Cli.Models;

public sealed class MigrationDefinition
{
    public required string Description { get; init; }
    public required List<MigrationStep> Up { get; init; }
    public required List<MigrationStep> Down { get; init; }
}

public sealed class MigrationStep
{
    public required string Type { get; init; }
    public string? Key { get; init; }
    public string? Value { get; init; }
    public List<string>? Keys { get; init; }
    public Dictionary<string, string>? Values { get; init; }
}
