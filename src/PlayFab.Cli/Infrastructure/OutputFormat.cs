namespace PlayFab.Cli.Infrastructure;

/// <summary>CLI output format selected via --output.</summary>
public enum OutputFormat
{
    /// <summary>JSON output, pipeable. Syntax-highlighted when interactive.</summary>
    Json,

    /// <summary>Spectre.Console table for human reading.</summary>
    Table,
}
