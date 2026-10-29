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

**Step syntax** — key/value operations (`*-title-data`) use one of three mutually exclusive forms; statistic and leaderboard operations use a structured, name-based form instead:

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

# Statistic definition — single-column shorthand (create-statistic/delete-statistic only):
- type: create-statistic
  name: PlayerScore
  aggregationMethod: Sum       # Last | Min | Max | Sum
  resetInterval: Month         # Manual | Hour | Day | Week | Month (default: Manual)
  maxQueryableVersions: 3      # default: 1
  entityType: title_player_account  # default: title_player_account

# Statistic definition — multi-column form (mutually exclusive with aggregationMethod):
- type: create-statistic
  name: RaceResult
  columns:
    - name: Time
      aggregationMethod: Min
    - name: Distance
      aggregationMethod: Max

# Statistic deletion — name only, no other fields allowed:
- type: delete-statistic
  name: PlayerScore

# Leaderboard definition (V2 Progression API) — columns are always explicit:
- type: create-leaderboard
  name: TopScores
  sizeLimit: 1000              # default: 1000
  resetInterval: Month         # Manual | Hour | Day | Week | Month (default: Manual)
  maxQueryableVersions: 3      # default: 1
  entityType: title_player_account  # default: title_player_account
  leaderboardColumns:
    - name: Score
      sortDirection: Descending  # Ascending | Descending
      linkedStatistic: PlayerScore          # optional — must be paired with linkedStatisticColumn
      linkedStatisticColumn: Value          # the column within that statistic to source values from
    - name: Rank
      sortDirection: Ascending   # plain column, no link — mixing linked and plain columns is fine

# Leaderboard deletion — name only, no other fields allowed:
- type: delete-leaderboard
  name: TopScores
```

A leaderboard column may optionally link to an existing statistic's column via `linkedStatistic`/`linkedStatisticColumn` — both must be set together or both omitted. PlayFab keeps linked columns in sync with the statistic automatically. The loader only validates field shape/pairing, not that the referenced statistic actually exists — PlayFab rejects a bad reference at `migrate up`.

**Operation types:**
- Implemented: `set-title-data`, `delete-title-data`, `set-title-internal-data`, `delete-title-internal-data`, `create-statistic`, `delete-statistic`, `create-leaderboard`, `delete-leaderboard`
- Defined but not yet implemented: `reset-statistic`, `set-policy`

`create-statistic`/`delete-statistic` and `create-leaderboard`/`delete-leaderboard` use PlayFab's Progression API (`PlayFabProgressionAPI`), which requires an entity token in addition to the secret key — `MigrateCommands` acquires this via `PlayFabAuth.RequireEntityCredentialsAsync` before running `up`/`down`.

Constants and sets live in `Models/OperationTypes.cs`. To add a new operation type: add the constant, add it to `All` (and `Implemented` when ready), then add the dispatch case in `MigrationRunner.ExecuteSingleAsync` (key/value ops), `ExecuteStatisticStepAsync` (`OperationTypes.StatisticOperations`), or `ExecuteLeaderboardStepAsync` (`OperationTypes.LeaderboardOperations`).

**Key infrastructure classes:**
- `MigrationLoader` — discovers, parses (YamlDotNet), and validates YAML files (including batch form and statistic/leaderboard-step validation)
- `MigrationLedgerService` — reads/writes the ledger JSON from PlayFab Title Internal Data
- `MigrationRunner` — executes up/down steps via PlayFab Admin API (expands batch steps into individual API calls) and Progression API (statistic and leaderboard definitions), updates ledger. Every PlayFab call goes through `ExecuteWithThrottleRetryAsync`, which retries on `PlayFabErrorCode.APIClientRequestRateLimitExceeded` using the server-specified `PlayFabError.RetryAfterSeconds` cooldown (up to 5 attempts) rather than a guessed delay — large migrations (e.g. a generated batch of hundreds of statistics/leaderboards) can otherwise get throttled. `create-statistic`/`create-leaderboard` additionally treat "already exists" (`DuplicateStatisticName`/`LeaderboardNameConflict`) as success, so a `migrate up` retried after a partial failure resumes cleanly instead of failing immediately on the first already-created definition — the ledger is only written after *all* steps succeed, so a failed run's successfully-created definitions aren't otherwise recorded anywhere.
- `PlayFabAuth` — resolves credentials, configures `PlayFabSettings.staticSettings`, and (`RequireEntityCredentialsAsync`) acquires the entity token Progression API calls need

### Synthetic Players, Statistic Seeding & Leaderboard Readback

`playfab players create/delete/list` manages synthetic test player accounts (ADR-0002), tracked per-title in `~/.playfab/synthetic-players-{TITLEID}.json` via `SyntheticPlayerService`/`SyntheticPlayerStore`. `create` sets the display name twice — `UpdateUserTitleDisplayNameAsync` (classic) and `PlayFabProfilesAPI.SetDisplayNameAsync` (Entity Profile) — because Progression/Leaderboard APIs read the Entity Profile name, not the classic one; this requires `RequireEntityCredentialsAsync`, not just the secret key.

`playfab stats seed --statistic <name> --column <Name:min-max> [--column ...] (--all | --player-id <id> ...) [--count N]` writes randomized values into selected columns of an existing statistic for tracked synthetic players only (same safety rule as `players delete`: untracked IDs are rejected). PlayFab's `UpdateStatisticsAsync` takes a full positional `Scores` list per statistic (ordered to match the statistic's defined columns), not a named partial update — `StatisticSeedService` implements "seed only some columns" as **read-merge-write**: fetch the player's current scores (`GetStatisticsAsync`), overwrite only the targeted positions with a fresh `Random.Shared.Next(min, max + 1)`, and submit the full merged list back. Column existence is validated against the statistic's live `GetStatisticDefinitionAsync` result, not local migration YAML.

`--count N` (default 1) repeats the write N times per player, each with an independently randomized value — e.g. to build up a `Sum` column's running total or try several `Min`/`Max` rolls. Whether a given `--column` run is still visible after N writes depends on its aggregation method: `Sum` accumulates, `Min`/`Max` settle to the extreme across all N, `Last` keeps only the final write (the other N-1 leave no trace). PlayFab's docs confirm `UpdateStatistics` aggregates incoming `Scores` per column's configured method rather than overwriting, so `ValidateUntouchedSumColumns` refuses to seed a statistic that has a `Sum` column *not* included in `--column` — resending that column's existing value every write would double it. `Min`/`Max`/`Last` columns left untouched are safe (resending the same value is always a no-op for those), so they aren't restricted.

`playfab stats get --statistic <name> (--all | --player-id <id> ...)` reads a statistic's current stored value straight from `GetStatisticsAsync`, bypassing Game Manager — useful for verifying a seed actually landed when the UI doesn't seem to reflect it (e.g. to tell apart a real write-path bug from a Game Manager display/propagation gap). Shows each player's `Version` and per-column values (`—` where no value is recorded yet), plus the statistic's current definition version with a warning if any player's value version doesn't match it.

`playfab stats definition --statistic <name>` displays the statistic's *definition* (not values) straight from `GetStatisticDefinitionAsync`: entity type, current version, reset interval, max queryable versions, created/last-reset timestamps, linked leaderboard names, and each column's aggregation method. The `linkedLeaderboardNames` field is the quickest way to check whether a statistic has any leaderboard attached to it at all.

### Statistic Definition Files (Desired-State)

Statistic definitions can be stored as standalone YAML files in a `statistics/` folder (one file per statistic), serving as the desired-state source of truth for what statistics should exist on a PlayFab title. These are separate from migration files and use a dedicated strongly-typed model.

**File format** — `statistics/<Name>.yml`:

```yaml
name: PlayerScore
entityType: title_player_account
resetInterval: Month
maxQueryableVersions: 3
columns:
  - name: Value
    aggregationMethod: Sum
    type: number             # string (default) | number | bool
```

Only desired-state fields are stored. Runtime metadata (version, created, lastResetTime, linkedLeaderboardNames) is excluded — it belongs to PlayFab and would drift. The `type` field on columns is a local semantic annotation (PlayFab doesn't have a column type concept); it defaults to `string` when omitted.

**`playfab stats pull --statistic <name> [--statistics-dir <path>]`** fetches a single statistic definition from PlayFab via `GetStatisticDefinitionAsync` and writes it as a YAML file. **`playfab stats pull --all [--statistics-dir <path>]`** fetches all statistics via `ListStatisticDefinitionsAsync` (paginated). `--statistics-dir` defaults to `statistics/`. Overwrites existing files (pull is always a fresh snapshot).

**Key classes:**
- `StatisticDefinitionFile` / `StatisticColumnDefinition` (`Models/StatisticDefinitionFile.cs`) — strongly-typed model, separate from `MigrationStep`
- `StatisticDefinitionStore` (`Infrastructure/StatisticDefinitionStore.cs`) — load/save/validate YAML files, map from PlayFab API response
- `ValidationConstants` (`Models/ValidationConstants.cs`) — shared validation sets (aggregation methods, reset intervals, sort directions) used by both `MigrationLoader` and `StatisticDefinitionStore`
- `StatisticReconciler` (`Infrastructure/StatisticReconciler.cs`) — pure comparison logic: takes local and remote definition lists, returns a list of `ReconcileAction`s (Create/Delete/Recreate/Synced/VersionConfigDrift)
- `MigrationGenerator` (`Infrastructure/MigrationGenerator.cs`) — converts a `ReconcileResult` into a `MigrationDefinition` with symmetric up/down steps

**`playfab migrate plan statistics [--statistics-dir <path>] [--include-deletes] [--allow-recreate]`** compares local definition files against live PlayFab state and generates a migration file for the differences. Inherits `--migrations-dir` from the `migrate` group. Reconciliation rules:
- Local-only statistic → `create-statistic` step
- PlayFab-only statistic → warned unless `--include-deletes` is set (then `delete-statistic`)
- Column/entity-type mismatch → warned unless `--allow-recreate` is set (then destructive delete+create pair — loses player data)
- Version config drift (resetInterval/maxQueryableVersions) → warned only (`update-statistic` not yet implemented)
- Matching → synced, no action

### Leaderboard Definition Files (Desired-State)

Leaderboard definitions can be stored as standalone YAML files in a `leaderboards/` folder (one file per leaderboard), serving as the desired-state source of truth — same pattern as statistic definitions.

**File format** — `leaderboards/<Name>.yml`:

```yaml
name: TopScores
entityType: title_player_account
resetInterval: Month
maxQueryableVersions: 3
sizeLimit: 500
columns:
  - name: Score
    sortDirection: Descending
    linkedStatistic: PlayerScore          # optional — paired with linkedStatisticColumn
    linkedStatisticColumn: Value
  - name: Rank
    sortDirection: Ascending
```

Only desired-state fields are stored. Runtime metadata (version, created, lastResetTime) is excluded. Column `linkedStatistic`/`linkedStatisticColumn` must both be set or both omitted — the loader validates pairing but not that the referenced statistic exists (PlayFab rejects bad references at `migrate up`).

**`playfab leaderboard pull --name <name> [--leaderboards-dir <path>]`** fetches a single leaderboard definition from PlayFab via `GetLeaderboardDefinitionAsync` and writes it as a YAML file. **`playfab leaderboard pull --all [--leaderboards-dir <path>]`** fetches all leaderboards via `ListLeaderboardDefinitionsAsync` (paginated). `--leaderboards-dir` defaults to `leaderboards/`. Overwrites existing files (pull is always a fresh snapshot).

**Key classes:**
- `LeaderboardDefinitionFile` / `LeaderboardColumnDefinition` (`Models/LeaderboardDefinitionFile.cs`) — strongly-typed model, separate from `MigrationStep`
- `LeaderboardDefinitionStore` (`Infrastructure/LeaderboardDefinitionStore.cs`) — load/save/validate YAML files, map from PlayFab API response, paginated fetch
- `LeaderboardReconciler` (`Infrastructure/LeaderboardReconciler.cs`) — pure comparison logic: takes local and remote definition lists, returns a list of `LeaderboardReconcileAction`s (Create/Delete/Recreate/Synced/VersionConfigDrift). Column comparison includes sort direction and linked statistic fields (case-insensitive). `SizeLimit` is compared as version config (warn-only drift).
- `MigrationGenerator.GenerateLeaderboardMigration` (`Infrastructure/MigrationGenerator.cs`) — converts a `LeaderboardReconcileResult` into a `MigrationDefinition` with symmetric up/down steps

**`playfab migrate plan leaderboards [--leaderboards-dir <path>] [--name <name>] [--include-deletes] [--allow-recreate]`** compares local definition files against live PlayFab state and generates a migration file for the differences. Inherits `--migrations-dir` from the `migrate` group. `--name` filters to a single leaderboard. Reconciliation rules mirror statistics:
- Local-only leaderboard → `create-leaderboard` step
- PlayFab-only leaderboard → warned unless `--include-deletes` is set (then `delete-leaderboard`)
- Column/entity-type mismatch → warned unless `--allow-recreate` is set (then destructive delete+create pair — loses data)
- Version config drift (resetInterval/maxQueryableVersions/sizeLimit) → warned only (`update-leaderboard` not yet implemented)
- Matching → synced, no action

`playfab leaderboard get --name <name> [--page-size N] [--starting-position N] [--version N]` reads a leaderboard's ranked entries straight from `PlayFabProgressionAPI.GetLeaderboardAsync` (`Commands/Leaderboard/LeaderboardCommands.cs`) — unlike `stats get`, this isn't scoped to tracked synthetic players; it returns a page of the real ranking (rank, entity ID, display name, per-column scores), matching how Game Manager itself would read it.

`playfab leaderboard definition --name <name>` displays the leaderboard's *definition* straight from `GetLeaderboardDefinitionAsync`: entity type, current version, size limit, reset interval, max queryable versions, created/last-reset timestamps, and each column's sort direction plus linked statistic (if any).

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

Tests use xunit and mirror the src folder structure (`Commands/`, `Infrastructure/`, `Models/`). Environment variable tests use `[Collection("Environment")]` for sequential execution. `CliConfig` tests use temp directories. `CredentialResolver` tests pass explicit `CliConfig` instances. `MigrationLoader` tests use temp directories with YAML files. `StatisticDefinitionStore` and `LeaderboardDefinitionStore` tests use temp directories and cover load/save/validate/map logic. `StatisticReconciler` and `LeaderboardReconciler` tests cover all reconciliation scenarios (create/delete/recreate/synced/drift, flag gating, case insensitivity). `MigrationGenerator` tests cover both statistic and leaderboard migration generation. Runner, LedgerService, and the live-API parts of `SyntheticPlayerService`/`StatisticSeedService` are not unit-tested (they make PlayFab API calls) — only their pure logic (e.g. `StatisticSeedService.ParseColumnSpec`/`ValidateColumns`/`MergeScores`) is.
