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

An infrastructure-as-code CLI tool (`playfab`) that configures PlayFab titles from declarative configuration files (`playfab.title.json`). Packaged as a `dotnet tool` via NuGet. Targets `net10.0` with nullable enabled.

There are two distinct configuration concepts:
- **Credentials file** (`~/.playfab/config.json`) — stores PlayFab Title ID and Secret Key for authentication
- **Title configuration file** (`playfab.title.json`) — declares the desired state of a PlayFab title (title data, statistics, leaderboards, policies). This is the infrastructure-as-code file that lives in your project directory.

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

Commands follow `playfab <group> <verb>` (e.g., `playfab config show`). Each command group is a static class in `Commands/<Group>/<Group>Commands.cs` with:

```csharp
public static Command CreateGroup(GlobalOptions globalOptions)
```

`GlobalOptions` is a record in `Infrastructure/GlobalOptions.cs` that bundles the three recursive options (`--title-id`, `--secret-key`, `--output`). To add a new command group: create the static class, then add one `rootCommand.Add()` call in `Program.cs`.

### Credential Resolution (`CredentialResolver`)

Credentials resolve in priority order: CLI args → env vars (`PLAYFAB_TITLE_ID`, `PLAYFAB_SECRET_KEY`) → config file (`~/.playfab/config.json`). The `CredentialResolver.Resolve()` method implements this cascade and returns a `ResolvedCredentials` record. It accepts an optional `CliConfig` parameter for testability.

### Title Configuration File (`playfab.title.json`)

The `TitleConfiguration` model in `Models/TitleConfiguration.cs` defines the schema skeleton. Sections use `JsonNode?` types as placeholders — each section gets a proper typed model when its command group is implemented. Current sections: `titleData`, `titleInternalData`, `statistics`, `leaderboards`, `policies`.

To add a new section: add a `JsonNode?` property to `TitleConfiguration`, then replace it with a typed model when the feature is ready.

### Output

All commands support `--output Json` (default, pipeable with Spectre syntax highlighting for interactive terminals) and `--output Table` (Spectre.Console). `OutputHelper` centralizes both paths.

### Key Dependencies

- `System.CommandLine` 2.0 — CLI parsing
- `PlayFabAllSDK` — PlayFab Admin + Progression APIs
- `Spectre.Console` / `Spectre.Console.Json` — table and JSON output formatting
- `xunit` — test framework

## Branch & CI

- Main branch: `master` (squash-merge only, requires `build` status check)
- Release tags (`v*`) trigger the GitHub Actions workflow that builds self-contained binaries (win-x64, linux-x64, osx-x64, osx-arm64) and a NuGet package, then creates a GitHub Release.

## Testing

Tests use xunit and mirror the src folder structure (`Commands/`, `Infrastructure/`, `Models/`). Tests that manipulate environment variables use `[Collection("Environment")]` for sequential execution. `CliConfig` tests use temp directories via the `configDirectory` parameter to avoid touching `~/.playfab/`. `CredentialResolver` tests pass explicit `CliConfig` instances to avoid reading real credentials.
