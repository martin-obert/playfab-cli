namespace PlayFab.Cli.Models;

public sealed record MigrationRunResult(bool Success, string? ErrorMessage, int StepsExecuted);
