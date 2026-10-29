namespace PlayFab.Cli.Models;

public sealed record MigrationFile(
    string Id,
    string Name,
    string FilePath,
    MigrationDefinition Definition);
