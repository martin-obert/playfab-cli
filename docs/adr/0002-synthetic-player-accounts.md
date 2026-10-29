# ADR-0002: Synthetic Player Account Management

**Status:** Accepted  
**Date:** 2026-10-29  
**Context:** Dev/test workflows require seeding PlayFab titles with test player accounts and cleaning them up afterward

## Decision

Add a `playfab players` command group with `create`, `delete`, and `list` subcommands for managing synthetic test player accounts.

## Command Structure

```
playfab players create --count <N>      # Create N accounts (default 1, max 100)
playfab players delete --all            # Delete all tracked accounts for this title
playfab players delete --player-id <id> # Delete a single tracked account
playfab players list                    # List tracked accounts from local JSON
```

## API Surface

- **Create**: `PlayFabServerAPI.LoginWithServerCustomIdAsync` with `CreateAccount = true` to create the player, then `PlayFabAdminAPI.UpdateUserTitleDisplayNameAsync` to set the classic display name, then `PlayFabProfilesAPI.SetDisplayNameAsync` to set the Entity Profile's display name — Progression/Leaderboard APIs read the latter, not the classic one, so both calls are needed for the name to show up there. The second call requires an entity token (`PlayFabAuth.RequireEntityCredentialsAsync`), not just the secret key.
- **Delete**: `PlayFabAdminAPI.DeletePlayerAsync` (title-scoped deletion).
- **Display names**: Generated via `Bogus` NuGet package (`Faker.Internet.UserName()`), truncated to 25 chars (PlayFab limit). Retries up to 3 times on name conflicts.

## Persistence

Created accounts are stored per-title in `~/.playfab/synthetic-players-{TITLEID}.json`:

```json
{
  "titleId": "ABCD",
  "players": [
    {
      "playFabId": "A1B2C3D4E5F6",
      "serverCustomId": "synth-3f8a1b2c...",
      "displayName": "Josie_Marks42",
      "createdAt": "2026-10-29T14:30:00Z"
    }
  ]
}
```

Per-title files because PlayFab player databases are title-scoped. The file is saved after each successful account creation/deletion for crash resilience.

## Error Handling

- **Partial creation**: On API failure mid-batch, already-created accounts are persisted and the loop stops with a progress report (`3/10 account(s) created`).
- **Delete safety**: `--player-id` only deletes accounts found in the local tracking file — prevents accidental deletion of real players.
- **Rate limiting**: 100ms delay between API calls when operating on more than 10 accounts.

## Consequences

- Adds `Bogus` as a new NuGet dependency (popular, well-maintained fake data library).
- Uses `PlayFabServerAPI` (in addition to the existing `PlayFabAdminAPI`) — both are included in the `PlayFabAllSDK` package already referenced.
- Orphaned accounts: If the local JSON file is deleted, there is no way to rediscover the accounts through this tool. Manual cleanup is possible via the PlayFab Game Manager dashboard.
