# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
dotnet build                                            # Build entire solution
dotnet test                                             # Run all tests
dotnet test --filter "FullyQualifiedName~TestClassName.MethodName"  # Single test
dotnet run --project src/PlayFab.Cli -- --help          # Run CLI locally
dotnet pack src/PlayFab.Cli/PlayFab.Cli.csproj -o artifacts  # Package as dotnet tool
```

Requires .NET 10 SDK.

## Architecture

A migration-based CLI tool (`playfab`) for configuring PlayFab titles, similar to database migrations (EF Core, Flyway). Packaged as a `dotnet tool` via NuGet. Targets `net10.0` with nullable enabled.

### Two Distinct Configuration Concepts

- **Credentials file** (`~/.playfab/config.json`) — stores PlayFab Title ID and Secret Key
- **Migration files** (`migrations/*.yml`) — YAML files defining up/down steps that modify a PlayFab title's configuration. The ledger of applied migrations is stored as PlayFab Title Internal Data under key `__playfab_cli_migrations`.

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

Commands follow `playfab <group> <verb>` (e.g., `playfab migrate up`). Each command group is a static class in `Commands/<Group>/<Group>Commands.cs` with:

```csharp
public static Command CreateGroup(GlobalOptions globalOptions)
```

`GlobalOptions` is a record in `Infrastructure/GlobalOptions.cs` that bundles the three recursive options (`--title-id`, `--secret-key`, `--output`). To add a new command group: create the static class, then add one `rootCommand.Add()` call in `Program.cs`.

### Migration System

Migration files use YAML format with timestamp-prefixed filenames (`20261028120000_add_data.yml`). The ID is parsed from the filename. Each file has `description`, `up` (list of steps), and `down` (list of steps).

**Step syntax** — three forms, mutually exclusive per step:

```yaml
# Single key (set operations require value, delete operations forbid it):
- type: set-title-data
  key: GameVersion
  value: "1.0.0"

# Batch set — values dictionary (set-* operations only):
- type: set-title-data
  values:
    GameVersion: "1.0.0"
    MatchmakingConfig: '{"maxPlayers": 10}'

# Batch delete — keys list (delete-* operations only):
- type: delete-title-data
  keys:
    - GameVersion
    - MatchmakingConfig
```

**Operation types:**
- Implemented: `set-title-data`, `delete-title-data`, `set-title-internal-data`, `delete-title-internal-data`
- Defined but not yet implemented: `create-statistic`, `delete-statistic`, `reset-statistic`, `create-leaderboard`, `delete-leaderboard`, `set-policy`

Constants and sets live in `Models/OperationTypes.cs`. To add a new operation type: add the constant, add it to `All` (and `Implemented` when ready), then add the dispatch case in `MigrationRunner.ExecuteSingleAsync`.

**Key infrastructure classes:**
- `MigrationLoader` — discovers, parses (YamlDotNet), and validates YAML files (including batch form validation)
- `MigrationLedgerService` — reads/writes the ledger JSON from PlayFab Title Internal Data
- `MigrationRunner` — executes up/down steps via PlayFab Admin API (expands batch steps into individual API calls), updates ledger
- `PlayFabAuth` — resolves credentials and configures `PlayFabSettings.staticSettings`

### Credential Resolution (`CredentialResolver`)

Priority: CLI args → env vars (`PLAYFAB_TITLE_ID`, `PLAYFAB_SECRET_KEY`) → config file (`~/.playfab/config.json`).

### Key Dependencies

- `System.CommandLine` 2.0 — CLI parsing
- `PlayFabAllSDK` — PlayFab Admin APIs
- `Spectre.Console` / `Spectre.Console.Json` — table and JSON output formatting
- `YamlDotNet` — YAML migration file parsing
- `xunit` — test framework

## Branch & CI

- Main branch: `master` (squash-merge only, requires `build` status check)
- Release tags (`v*`) trigger GitHub Actions: self-contained binaries + NuGet package → GitHub Release.

## Testing

Tests use xunit and mirror the src folder structure (`Commands/`, `Infrastructure/`, `Models/`). Environment variable tests use `[Collection("Environment")]` for sequential execution. `CliConfig` tests use temp directories. `CredentialResolver` tests pass explicit `CliConfig` instances. `MigrationLoader` tests use temp directories with YAML files. Runner and LedgerService are not unit-tested (they make PlayFab API calls).
