# ADR-0001: Partial Migration Failure Handling

**Status:** Proposed  
**Date:** 2026-10-29  
**Context:** Migration runner executes multi-step migrations without tracking partial progress

## Problem

When a migration with multiple steps fails mid-flight (e.g., step 2 of 3 fails), the current implementation leaves the system in an inconsistent state:

1. **Phantom side effects** — Steps that succeeded before the failure have already mutated PlayFab title state (e.g., `SetTitleData` calls), but the ledger records no trace of them. The migration appears as "never applied."

2. **Non-idempotent retry** — Re-running `migrate up` replays all steps from the beginning, including those that already succeeded. For idempotent operations like `set-title-data` this is harmless, but future operations like `create-statistic` or `create-leaderboard` would fail on duplicate creation.

3. **No recovery path** — The user cannot `migrate down` because the ledger doesn't know the migration was partially applied. Manual cleanup on PlayFab is the only option.

4. **Symmetric rollback issue** — `RollbackAsync` has the same pattern: a partial rollback leaves the ledger entry intact, but some steps have already been reverted.

## Relevant Code

- `MigrationRunner.ApplyAsync` (lines 9–38): Ledger is only updated after all steps succeed.
- `MigrationRunner.RollbackAsync` (lines 41–61): Ledger entry is only removed after all rollback steps succeed.
- `MigrationRecord`: Contains no status or progress tracking fields.

## Proposed Solution

Track partial migration state in the ledger:

- Add `Status` field to `MigrationRecord` with values: `Pending`, `Applied`, `Failed`.
- Add `StepsCompleted` field (int) to record progress.
- **Before** executing steps, write a `Pending` ledger entry with `StepsCompleted = 0`.
- **On failure**, update the entry to `Failed` with the count of completed steps.
- **On success**, update the entry to `Applied`.
- **On retry**, skip steps up to `StepsCompleted` (resume from where it left off).
- **On rollback of a failed migration**, only undo the steps that actually ran (0..StepsCompleted).

## Consequences

- Ledger schema changes — existing ledgers without `Status`/`StepsCompleted` need a default (`Applied` / step count from definition).
- Extra PlayFab API calls to update ledger mid-migration (one write before, one on failure/success).
- The `migrate status` command should surface `Failed` migrations distinctly from `Applied` or `Pending`.
