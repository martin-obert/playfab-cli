namespace PlayFab.Cli.Models;

public sealed class MigrationDefinition
{
    public string Description { get; set; } = "";
    public List<MigrationStep> Up { get; set; } = [];
    public List<MigrationStep> Down { get; set; } = [];
}

public sealed class MigrationStep
{
    public string Type { get; set; } = "";
    public string Key { get; set; } = "";
    public string? Value { get; set; }
}
