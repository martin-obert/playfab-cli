# PlayFab Feature Implementation Checklist

Step-by-step guide for adding support for a new PlayFab feature (e.g., leaderboards, policies) to the CLI. The statistics feature is the reference implementation — file paths point to its concrete code.

Throughout this document, `Xxx` is a placeholder for the feature name (e.g., `Leaderboard`, `Statistic`).

---

## 1. Migration Infrastructure (shared, add once per feature)

These pieces wire the feature into the existing migration system so that `migrate up` / `migrate down` can execute its steps.

- [ ] **Operation type constants** — `Models/OperationTypes.cs`
  - Add `CreateXxx` and `DeleteXxx` string constants
  - Add them to the `All` set
  - Add them to the `Implemented` set (once the runner dispatch exists)
  - Create an `XxxOperations` HashSet containing both
  - _Reference: `CreateStatistic` / `DeleteStatistic` / `StatisticOperations`_

- [ ] **Column step model** — `Models/MigrationDefinition.cs`
  - Add a `XxxColumnStep` class with `required` properties for the feature's per-column fields
  - Add a nullable `List<XxxColumnStep>?` property to `MigrationStep`
  - _Reference: `StatisticColumnStep` (Name + AggregationMethod), `LeaderboardColumnStep` (Name + SortDirection + optional linked statistic)_

- [ ] **Validation constants** — `Models/ValidationConstants.cs`
  - Add any new `HashSet<string>` sets for feature-specific enum values (e.g., sort directions, aggregation methods)
  - Use `StringComparer.OrdinalIgnoreCase`
  - _Reference: `ValidAggregationMethods`, `ValidSortDirections`_

- [ ] **Migration loader validation** — `Infrastructure/MigrationLoader.cs`
  - Add a `ValidateXxxStep(MigrationStep, string prefix, List<string> errors)` private method
  - Wire it from the main `ValidateStep` method via `OperationTypes.XxxOperations.Contains(step.Type)`
  - Validate: Name required, no key/keys/values fields, delete variant forbids columns/config fields, create variant requires columns
  - _Reference: `ValidateStatisticStep`, `ValidateLeaderboardStep`_

- [ ] **Migration runner dispatch** — `Infrastructure/MigrationRunner.cs`
  - Add `ExecuteXxxStepAsync(MigrationStep)` — switch on `step.Type` to route create/delete
  - Add `CreateXxxDefinitionAsync(MigrationStep)` — build API request, call PlayFab, handle idempotent "already exists" error
  - Add `DeleteXxxDefinitionAsync(string name)` — call PlayFab delete API
  - Wire from `ExecuteStepAsync` via `OperationTypes.XxxOperations.Contains(step.Type)`
  - Use `ExecuteWithThrottleRetryAsync` for all PlayFab calls
  - _Reference: `ExecuteStatisticStepAsync`, `CreateStatisticDefinitionAsync`, `ExecuteLeaderboardStepAsync`_

---

## 2. Definition File Model

A strongly-typed model representing a single feature definition as a YAML file. This is the desired-state source of truth — separate from `MigrationStep`.

- [ ] **Definition file class** — `Models/XxxDefinitionFile.cs`
  - `required string Name`
  - `string EntityType` (default: `"title_player_account"`)
  - `string ResetInterval` (default: `"Manual"`)
  - `int MaxQueryableVersions` (default: `1`)
  - `required List<XxxColumnDefinition> Columns`
  - Any feature-specific fields (e.g., `SizeLimit` for leaderboards)
  - All properties: public get/init, defaults on optional fields
  - _Reference: `StatisticDefinitionFile`, `StatisticColumnDefinition`_

- [ ] **Column definition class** — same file
  - `required string Name`
  - Feature-specific fields (e.g., `AggregationMethod` for statistics, `SortDirection` for leaderboards)
  - Optional local-only fields (e.g., `Type` with default `"string"`) — these are excluded from reconciliation comparison
  - _Reference: `StatisticColumnDefinition`_

---

## 3. Definition Store

Static class that handles file I/O, validation, and mapping between PlayFab API responses and definition file models.

- [ ] **`Infrastructure/XxxDefinitionStore.cs`** — static class with:

  - [ ] `LoadAll(string definitionsDir)` — find `*.yml` + `*.yaml`, order by filename, deserialize each
  - [ ] `LoadSingle(string filePath)` — deserialize a single file, throw if not found
  - [ ] `Save(XxxDefinitionFile definition, string definitionsDir)` — serialize to YAML, write as `{Name}.yml`
  - [ ] `Validate(XxxDefinitionFile definition)` — return `List<string>` of errors:
    - Name non-empty
    - EntityType non-empty
    - ResetInterval in `ValidationConstants.ValidResetIntervals`
    - MaxQueryableVersions >= 1
    - Columns non-empty, unique names (case-insensitive), valid per-column enum values
    - Feature-specific constraints
  - [ ] `MapFromPlayFab(GetXxxDefinitionResponse response)` — API response → `XxxDefinitionFile`
  - [ ] `MapFromPlayFab(XxxDefinition definition)` — list-item overload (for paginated list responses)
  - [ ] `FetchAllFromPlayFabAsync()` — paginated fetch using `ListXxxDefinitionsAsync` + `SkipToken` loop

  Use a private `IDeserializer` and `ISerializer` with `CamelCaseNamingConvention` and `OmitNull`.

  _Reference: `StatisticDefinitionStore.cs`_

---

## 4. Reconciler

Pure comparison logic — no API calls, no file I/O. Takes two lists (local + remote) and returns a result describing what actions are needed.

- [ ] **`Infrastructure/XxxReconciler.cs`** — static class with:

  - [ ] `Reconcile(List<XxxDefinitionFile> local, List<XxxDefinitionFile> remote, ReconcileOptions options)` → `ReconcileResult`
    - Match by name (case-insensitive)
    - Local-only → `ReconcileActionKind.Create`
    - Remote-only without `--include-deletes` → `Synced` with warning mentioning the flag
    - Remote-only with `--include-deletes` → `Delete`
    - Both exist, definitions match, version config matches → `Synced` (no warning)
    - Both exist, definitions match, version config differs → `VersionConfigDrift` with warning
    - Both exist, definitions don't match, without `--allow-recreate` → `VersionConfigDrift` with warning mentioning the flag
    - Both exist, definitions don't match, with `--allow-recreate` → `Recreate`
    - Sort results by name for deterministic output

  - [ ] `DefinitionsMatch(XxxDefinitionFile a, XxxDefinitionFile b)` — `internal`
    - Compare entity type (case-insensitive)
    - Compare columns by position: count, names, feature-specific enum values
    - Exclude local-only fields (e.g., `Type`) and version config fields

  - [ ] `VersionConfigMatches(XxxDefinitionFile a, XxxDefinitionFile b)` — `internal`
    - Compare `ResetInterval` and `MaxQueryableVersions` only

  Reuse the existing shared types: `ReconcileActionKind`, `ReconcileAction`, `ReconcileOptions`, `ReconcileResult` (defined in `StatisticReconciler.cs`).

  _Reference: `StatisticReconciler.cs`_

---

## 5. Migration Generator

Converts a `ReconcileResult` into a `MigrationDefinition` with symmetric up/down steps.

- [ ] **Add method to `Infrastructure/MigrationGenerator.cs`**:
  - `GenerateXxxMigration(ReconcileResult result, string description)` → `MigrationDefinition`
  - For each `ReconcileAction`:
    - **Create**: up = `create-xxx` (from local def), down = `delete-xxx`
    - **Delete**: up = `delete-xxx`, down = `create-xxx` (from remote def — preserves rollback)
    - **Recreate**: up = `delete-xxx` + `create-xxx` (local), down = `delete-xxx` + `create-xxx` (remote)
    - **Synced / VersionConfigDrift**: skip
  - Map `XxxDefinitionFile` → `MigrationStep`: Name, EntityType, ResetInterval, MaxQueryableVersions, columns list, feature-specific fields
  - Always use the explicit columns form, never single-column shorthand

  _Reference: `MigrationGenerator.GenerateStatisticMigration`_

---

## 6. CLI Commands — Read-back & Pull

Commands under `playfab <feature> <verb>` for interacting with live PlayFab data and pulling definitions.

- [ ] **Command group** — `Commands/Xxx/XxxCommands.cs`
  - `CreateGroup(GlobalOptions globalOptions)` returning a `Command`
  - Add `--xxx-dir` option (default: feature name, e.g., `"leaderboards"`) if the group needs a definitions directory

- [ ] **`pull` subcommand** — fetch definitions from PlayFab and write YAML files
  - `--name <name>` — pull a single definition by name
  - `--all` — pull all definitions (paginated)
  - `--xxx-dir <path>` — output directory (inherited from group)
  - Uses `XxxDefinitionStore.MapFromPlayFab` + `XxxDefinitionStore.Save`
  - For `--all`: uses `XxxDefinitionStore.FetchAllFromPlayFabAsync`

- [ ] **`definition` subcommand** — display a definition's metadata from PlayFab
  - `--name <name>` (required)
  - Table output: entity type, version, config fields, column details
  - JSON output: same fields

- [ ] **`get` subcommand** — read live data (rankings, values, etc.)
  - Feature-specific options (e.g., `--page-size`, `--version` for leaderboards; `--player-id`, `--all` for statistics)

- [ ] **(Optional) `seed` subcommand** — write test data for synthetic players

- [ ] **Register in `Program.cs`**
  - `rootCommand.Add(XxxCommands.CreateGroup(globalOptions));`

  _Reference: `StatsCommands.cs` (full group), `LeaderboardCommands.cs` (get + definition only)_

---

## 7. Migrate Plan Subcommand

Reconciliation command that compares local definition files against PlayFab and auto-generates a migration file.

- [ ] **Add to `Commands/Migrate/MigrateCommands.cs`**:
  - Create `CreatePlanXxxCommand(GlobalOptions, Option<string> migrationsDirOption)` — returns a `Command`
  - Wire into the existing `plan` group: `planGroup.Add(CreatePlanXxxCommand(...))`
  - Options:
    - `--xxx-dir` (default: feature directory name)
    - `--name` (optional: reconcile only this single definition)
    - `--include-deletes` (bool)
    - `--allow-recreate` (bool)

  - Handler flow:
    1. Load local definitions via `XxxDefinitionStore.LoadAll`
    2. Filter by `--name` if provided
    3. Validate each definition via `XxxDefinitionStore.Validate` — abort on errors
    4. `RequireEntityCredentialsAsync` (Progression API needs entity token)
    5. Fetch remote definitions via `XxxDefinitionStore.FetchAllFromPlayFabAsync`
    6. Filter remote by `--name` if provided
    7. `XxxReconciler.Reconcile(local, remote, options)`
    8. Print Spectre.Console table: Name, Status, Details
    9. If `result.HasChanges`: `MigrationGenerator.GenerateXxxMigration(result, description)` → write YAML
    10. Migration file naming: `{timestamp}_plan_xxx.yml`

  _Reference: `CreatePlanStatisticsCommand` in `MigrateCommands.cs`_

---

## 8. Tests

Follow the existing test structure — mirror the `src/` folder layout under `tests/PlayFab.Cli.Tests/`.

- [ ] **Model tests** — `tests/.../Models/XxxDefinitionFileTests.cs`
  - YAML serialization roundtrip
  - Default values applied correctly
  - All fields serialize/deserialize with camelCase naming
  - _Reference: `StatisticDefinitionFileTests.cs`_

- [ ] **Store tests** — `tests/.../Infrastructure/XxxDefinitionStoreTests.cs`
  - `LoadAll`: empty dir, single file, multiple files, ordering
  - `LoadSingle`: valid file, missing file throws
  - `Save`: writes correct YAML, creates directory
  - `Validate`: every error case (empty name, invalid enum values, duplicate column names, etc.)
  - `MapFromPlayFab`: both overloads, null handling, default values
  - Use temp directories for file I/O tests
  - _Reference: `StatisticDefinitionStoreTests.cs`_

- [ ] **Reconciler tests** — `tests/.../Infrastructure/XxxReconcilerTests.cs`
  - Create (local-only)
  - Delete with and without `--include-deletes`
  - Synced (matching definitions)
  - Column mismatch with and without `--allow-recreate`
  - Entity type mismatch
  - Version config drift (warn only)
  - Case-insensitive name matching
  - Case-insensitive column/enum matching
  - Results sorted by name
  - `HasChanges` for each action kind
  - `DefinitionsMatch` / `VersionConfigMatches` edge cases
  - _Reference: `StatisticReconcilerTests.cs`_

- [ ] **Migration generator tests** — `tests/.../Infrastructure/MigrationGeneratorTests.cs`
  - Add tests for `GenerateXxxMigration`:
    - Create action → correct up/down steps
    - Delete action → correct up/down steps (down preserves remote def)
    - Recreate action → delete+create in both directions
    - Synced/drift actions → skipped
    - All definition fields mapped correctly
    - Multiple actions → all included
  - _Reference: existing `MigrationGeneratorTests.cs`_

- [ ] **Command parsing tests** — `tests/.../Commands/XxxCommandParsingTests.cs`
  - Each subcommand parses without errors
  - Required options enforced
  - Optional options accepted
  - Global options propagate
  - _Reference: `StatsCommandParsingTests.cs`_

- [ ] **Migrate plan parsing tests** — `tests/.../Commands/MigrateCommandParsingTests.cs`
  - `migrate plan xxx` parses without args
  - Each option accepted: `--xxx-dir`, `--name`, `--include-deletes`, `--allow-recreate`
  - All flags together
  - Inherits `--migrations-dir` from `migrate` group
  - Inherits global options
  - _Reference: existing `MigratePlanStatistics*` tests_

---

## 9. Documentation

- [ ] **Update `CLAUDE.md`**:
  - Definition file YAML format with example
  - New CLI commands and their options
  - New infrastructure classes and their roles
  - Add to the testing section

---

## Quick Reference: Statistics Implementation Files

| Layer | File | Role |
|-------|------|------|
| Model | `Models/StatisticDefinitionFile.cs` | Desired-state definition + column model |
| Model | `Models/ValidationConstants.cs` | Shared enum validation sets |
| Model | `Models/OperationTypes.cs` | Operation type constants + sets |
| Model | `Models/MigrationDefinition.cs` | `MigrationStep` + `StatisticColumnStep` |
| Infra | `Infrastructure/StatisticDefinitionStore.cs` | Load/save/validate/map/fetch YAML files |
| Infra | `Infrastructure/StatisticReconciler.cs` | Pure comparison logic + result types |
| Infra | `Infrastructure/MigrationGenerator.cs` | Reconcile result → migration steps |
| Infra | `Infrastructure/MigrationRunner.cs` | Execute create/delete via PlayFab API |
| Infra | `Infrastructure/MigrationLoader.cs` | Validate migration YAML steps |
| Command | `Commands/Stats/StatsCommands.cs` | pull, get, definition, seed subcommands |
| Command | `Commands/Migrate/MigrateCommands.cs` | `migrate plan statistics` subcommand |
| Command | `Program.cs` | Registration: `rootCommand.Add(...)` |
| Test | `Tests/Models/StatisticDefinitionFileTests.cs` | Model serialization tests |
| Test | `Tests/Infrastructure/StatisticDefinitionStoreTests.cs` | Store unit tests |
| Test | `Tests/Infrastructure/StatisticReconcilerTests.cs` | Reconciler unit tests |
| Test | `Tests/Infrastructure/MigrationGeneratorTests.cs` | Generator unit tests |
| Test | `Tests/Infrastructure/StatisticSeedServiceTests.cs` | Seed service pure-logic tests |
| Test | `Tests/Commands/StatsCommandParsingTests.cs` | CLI parsing tests |
| Test | `Tests/Commands/MigrateCommandParsingTests.cs` | Migrate plan parsing tests |
