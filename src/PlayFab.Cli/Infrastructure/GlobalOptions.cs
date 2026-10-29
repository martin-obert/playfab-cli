using System.CommandLine;

namespace PlayFab.Cli.Infrastructure;

public sealed record GlobalOptions(
    Option<string?> TitleId,
    Option<string?> SecretKey,
    Option<OutputFormat> Output);
