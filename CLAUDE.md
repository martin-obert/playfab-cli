# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
dotnet build                                            # Build entire solution
dotnet test                                             # Run all tests
dotnet test --filter "FullyQualifiedName~CliConfigTests.MergeWith_CliArgs_TakePriority"  # Single test
dotnet run --project src/PlayFab.Cli -- --help          # Run CLI locally
dotnet pack src/PlayFab.Cli/PlayFab.Cli.csproj -o artifacts  # Package as dotnet tool
```

## Architecture

This is a .NET 10 CLI tool (`playfab`) for automating PlayFab operations, packaged as a `dotnet tool` via NuGet.

### CLI Framework: System.CommandLine 2.0 (stable)

The 2.0 stable API differs significantly from beta documentation found online:
- **`command.Add()`** — not `AddCommand()`/`AddOption()`
- **`command.SetAction(parseResult => ...)`** — not `SetHandler()` with parameter binding
- **`parseResult.GetValue(option)`** / **`parseResult.GetRequiredValue(option)`** — not handler delegates
- **`new Option<T>("--name", "--alias")`** — aliases go in constructor, not `AddAlias()`
- **`option.Required = true`** — not `IsRequired` in initializer
- **`command.Parse(args).InvokeAsync()`** — not `command.InvokeAsync(args)`
- **`Recursive = true`** on options to propagate them to subcommands

### Command Pattern

Commands follow `playfab <group> <verb>` (e.g., `playfab titledata get`). Each command group is a static class in `Commands/<Group>/` exposing a `CreateGroup()` method that returns a `Command` with subcommands. Global options (`--title-id`, `--secret-key`, `--output`) are defined in `Program.cs` with `Recursive = true` and threaded into command groups via method parameters.

To add a new command group: create `Commands/<Group>/<Group>Commands.cs` with a `CreateGroup()` static method, then add one `rootCommand.Add()` call in `Program.cs`.

### Configuration Cascade

`CliConfig` resolves credentials in priority order: CLI args → env vars (`PLAYFAB_TITLE_ID`, `PLAYFAB_SECRET_KEY`) → persisted file (`~/.playfab/config.json`). The `MergeWith()` method implements this cascade.

### Output

All commands support `--output Json` (default, pipeable) and `--output Table` (Spectre.Console). `OutputHelper` centralizes both paths — use `WriteJson()` for structured data and `WriteTable()` for key-value displays.

### PlayFab SDK

Uses `PlayFabAllSDK` with static settings (`PlayFabSettings.staticSettings`). `PlayFabClientFactory.Configure()` must be called before any API call. The SDK uses `PlayFab.AdminModels` / `PlayFab.ServerModels` namespaces — pick the right one based on the API being called.

## Testing

Tests use xunit. The test project references the CLI project directly for unit testing config and command parsing logic. Integration tests against live PlayFab APIs are not included — they require real credentials.
