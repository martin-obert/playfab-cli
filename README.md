# PlayFab.Cli

A .NET CLI tool for automating PlayFab game backend operations. Manage title data, API policies, and leaderboard/statistics definitions from your terminal or CI/CD pipeline.

## Installation

```bash
# Pack and install locally
dotnet pack src/PlayFab.Cli/PlayFab.Cli.csproj -o artifacts
dotnet tool install --global --add-source artifacts PlayFab.Cli

# Or run directly from source
dotnet run --project src/PlayFab.Cli -- --help
```

## Quick Start

```bash
# Save your credentials once
playfab config set --title-id YOUR_TITLE_ID --secret-key YOUR_SECRET_KEY

# Verify configuration
playfab config show
```

Credentials can also be set via environment variables (`PLAYFAB_TITLE_ID`, `PLAYFAB_SECRET_KEY`) or CLI flags (`--title-id`, `--secret-key`) on any command.

## Commands

| Command | Description |
|---------|-------------|
| `config set` | Save credentials to `~/.playfab/config.json` |
| `config show` | Display resolved configuration (secrets masked) |
| `titledata get` | Get title data (all keys or a specific key) |
| `titledata set` | Set a title data key-value pair |
| `policy get` | Get API policy statements |
| `leaderboard list` | List all statistic and leaderboard definitions |
| `leaderboard validate` | Validate a local schema file |
| `leaderboard sync` | Compare local schema with PlayFab, optionally apply |
| `leaderboard reset` | Manually reset a statistic or leaderboard version |
| `leaderboard seed` | Populate statistics/leaderboards with synthetic test data |

### Title Data

```bash
# Get all title data
playfab titledata get

# Get a specific key
playfab titledata get --key GameConfig

# Set a value
playfab titledata set --key GameConfig --value '{"maxPlayers": 10}'

# Table output
playfab titledata get --output Table
```

### API Policies

```bash
# View current API policy
playfab policy get

# As a formatted table
playfab policy get --output Table
```

### Leaderboard & Statistics Sync

Define statistics and leaderboards in a local JSON file and sync them to PlayFab. Dry-run by default — use `--apply` to push changes.

```bash
# Validate schema without contacting PlayFab
playfab leaderboard validate --file leaderboards.json

# Dry-run: see what would change
playfab leaderboard sync --file leaderboards.json

# Apply changes
playfab leaderboard sync --file leaderboards.json --apply

# Sync a single item by name
playfab leaderboard sync --file leaderboards.json --name TotalScore --apply

# List remote definitions
playfab leaderboard list
```

### Manual Reset

`resetInterval` in the schema only configures *automatic*, scheduled resets (`Hour`/`Day`/`Week`/`Month`). To reset a statistic or leaderboard on demand, use `reset` — it auto-detects whether `--name` refers to a statistic or a leaderboard, and resolves leaderboards that are linked to a statistic (PlayFab requires resetting the underlying statistic in that case, which cascades to the linked leaderboard). Dry-run by default — use `--apply` to perform it.

```bash
# Dry-run: see what would be reset
playfab leaderboard reset --name TotalScore

# Apply the reset
playfab leaderboard reset --name TotalScore --apply

# Disambiguate if a statistic and leaderboard share a name
playfab leaderboard reset --name TotalScore --type leaderboard --apply

# Safety check: only reset if the remote is still at the version you expect
playfab leaderboard reset --name TotalScore --version 3 --apply
```

### Synthetic Data Seeding

Populate statistics and leaderboards with test data using fake PlayFab accounts, useful for demos and testing without real players. Operates directly against remote definitions (no schema file needed). Fake accounts are created via `LoginWithServerCustomId` (secret-key only, no client login) and persisted locally at `~/.playfab/synthetic-accounts/{titleId}.json`, scoped per title — repeat runs reuse the same accounts and only create more if `--count` asks for more. Dry-run by default — use `--apply` to actually create accounts and push data.

Leaderboards linked to a statistic (`linkedStatistic` in the schema) can't be written to directly — PlayFab requires pushing to the underlying statistic instead, which cascades into the linked leaderboard. `seed` detects this automatically, same as `reset`.

```bash
# Dry-run: see what would be seeded, with how many accounts
playfab leaderboard seed

# Apply: create accounts (if needed) and push synthetic values
playfab leaderboard seed --apply

# Seed only one statistic or leaderboard
playfab leaderboard seed --name TotalScore --apply

# Reproducible values, custom range and account count
playfab leaderboard seed --count 200 --min 100 --max 5000 --seed 42 --apply
```

## Leaderboard Schema Format

The schema file (default: `leaderboards.json`) defines statistics and leaderboards:

```json
{
  "statistics": [
    {
      "name": "TotalScore",
      "aggregationMethod": "Max",
      "resetInterval": "Manual",
      "maxQueryableVersions": 1
    },
    {
      "name": "WeeklyWins",
      "aggregationMethod": "Sum",
      "resetInterval": "Week",
      "maxQueryableVersions": 3
    },
    {
      "name": "PlayerStats",
      "columns": [
        { "name": "Kills", "aggregationMethod": "Sum" },
        { "name": "Deaths", "aggregationMethod": "Sum" },
        { "name": "HighScore", "aggregationMethod": "Max" }
      ],
      "resetInterval": "Manual"
    }
  ],
  "leaderboards": [
    {
      "name": "TopScores",
      "columns": [
        {
          "name": "Score",
          "sortDirection": "Descending",
          "linkedStatistic": "TotalScore",
          "linkedStatisticColumn": "Value"
        }
      ],
      "sizeLimit": 1000,
      "resetInterval": "Manual"
    }
  ]
}
```

### Schema Reference

**Statistics:**

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `name` | string | required | Unique statistic name |
| `aggregationMethod` | enum | `Last` | `Last`, `Min`, `Max`, `Sum` (used when no columns defined) |
| `columns` | array | `[]` | Multi-column statistic; each has `name` + `aggregationMethod` |
| `resetInterval` | enum | `Manual` | `Manual`, `Hour`, `Day`, `Week`, `Month` |
| `maxQueryableVersions` | int | `1` | Number of historical versions to keep queryable |

When `columns` is empty, a single column named "Value" is created using the top-level `aggregationMethod`.

**Leaderboards:**

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `name` | string | required | Unique leaderboard name |
| `columns` | array | required | At least one column |
| `columns[].name` | string | required | Column display name |
| `columns[].sortDirection` | enum | `Descending` | `Descending` or `Ascending` |
| `columns[].linkedStatistic` | string | optional | Name of a statistic in this schema |
| `columns[].linkedStatisticColumn` | string | optional | Column name in the linked statistic |
| `sizeLimit` | int | `1000` | Max entries in the leaderboard |
| `resetInterval` | enum | `Manual` | Same options as statistics |
| `maxQueryableVersions` | int | `1` | Historical versions to keep queryable |

## Configuration

Credentials resolve in priority order:

1. CLI arguments (`--title-id`, `--secret-key`)
2. Environment variables (`PLAYFAB_TITLE_ID`, `PLAYFAB_SECRET_KEY`)
3. Config file (`~/.playfab/config.json`)

## Output Formats

All commands support `--output` (`-o`):

- **Json** (default) — machine-readable JSON. Syntax-highlighted in interactive terminals, plain when piped.
- **Table** — human-readable Spectre.Console table.

```bash
# Pipe JSON to jq
playfab titledata get | jq '.GameConfig'

# Human-readable table
playfab leaderboard list --output Table
```

## Use Cases

**CI/CD pipeline sync** — commit `leaderboards.json` to source control and sync on deploy:
```bash
playfab leaderboard sync --file leaderboards.json --apply \
  --title-id $PLAYFAB_TITLE_ID --secret-key $PLAYFAB_SECRET_KEY
```

**Multi-environment management** — same schema, different titles:
```bash
playfab leaderboard sync --file leaderboards.json --apply --title-id DEV_TITLE
playfab leaderboard sync --file leaderboards.json --apply --title-id PROD_TITLE
```

**PR validation** — validate schema in CI without credentials:
```bash
playfab leaderboard validate --file leaderboards.json
```

## Development

```bash
dotnet build                    # Build
dotnet test                     # Run tests
dotnet run --project src/PlayFab.Cli -- leaderboard --help
```

Requires .NET 10 SDK.
