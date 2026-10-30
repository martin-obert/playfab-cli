using PlayFab.Cli.Models;

namespace PlayFab.Cli.Infrastructure;

public sealed record LeaderboardReconcileAction(
    ReconcileActionKind Kind,
    string LeaderboardName,
    LeaderboardDefinitionFile? LocalDefinition,
    LeaderboardDefinitionFile? RemoteDefinition,
    string? Warning);

public sealed record LeaderboardReconcileResult(List<LeaderboardReconcileAction> Actions)
{
    public bool HasChanges => Actions.Any(a => a.Kind is ReconcileActionKind.Create
        or ReconcileActionKind.Delete or ReconcileActionKind.Recreate);
}

public static class LeaderboardReconciler
{
    public static LeaderboardReconcileResult Reconcile(
        List<LeaderboardDefinitionFile> local,
        List<LeaderboardDefinitionFile> remote,
        ReconcileOptions options)
    {
        var actions = new List<LeaderboardReconcileAction>();

        var remoteByName = remote.ToDictionary(d => d.Name, d => d, StringComparer.OrdinalIgnoreCase);
        var localByName = local.ToDictionary(d => d.Name, d => d, StringComparer.OrdinalIgnoreCase);

        foreach (var localDef in local)
        {
            if (!remoteByName.TryGetValue(localDef.Name, out var remoteDef))
            {
                actions.Add(new LeaderboardReconcileAction(ReconcileActionKind.Create, localDef.Name, localDef, null, null));
                continue;
            }

            if (DefinitionsMatch(localDef, remoteDef))
            {
                if (!VersionConfigMatches(localDef, remoteDef))
                {
                    actions.Add(new LeaderboardReconcileAction(ReconcileActionKind.VersionConfigDrift, localDef.Name, localDef, remoteDef,
                        $"Version config differs for '{localDef.Name}' (resetInterval/maxQueryableVersions/sizeLimit) — update-leaderboard not yet supported."));
                }
                else
                {
                    actions.Add(new LeaderboardReconcileAction(ReconcileActionKind.Synced, localDef.Name, localDef, remoteDef, null));
                }
                continue;
            }

            if (options.AllowRecreate)
            {
                actions.Add(new LeaderboardReconcileAction(ReconcileActionKind.Recreate, localDef.Name, localDef, remoteDef, null));
            }
            else
            {
                actions.Add(new LeaderboardReconcileAction(ReconcileActionKind.VersionConfigDrift, localDef.Name, localDef, remoteDef,
                    $"Column mismatch for '{localDef.Name}' — use --allow-recreate to generate delete+create steps (destructive: loses data)."));
            }
        }

        foreach (var remoteDef in remote)
        {
            if (localByName.ContainsKey(remoteDef.Name))
                continue;

            if (options.IncludeDeletes)
            {
                actions.Add(new LeaderboardReconcileAction(ReconcileActionKind.Delete, remoteDef.Name, null, remoteDef, null));
            }
            else
            {
                actions.Add(new LeaderboardReconcileAction(ReconcileActionKind.Synced, remoteDef.Name, null, remoteDef,
                    $"Leaderboard '{remoteDef.Name}' exists on PlayFab but has no local definition — use --include-deletes to generate a delete step."));
            }
        }

        return new LeaderboardReconcileResult(actions.OrderBy(a => a.LeaderboardName, StringComparer.OrdinalIgnoreCase).ToList());
    }

    internal static bool DefinitionsMatch(LeaderboardDefinitionFile a, LeaderboardDefinitionFile b)
    {
        if (!string.Equals(a.EntityType, b.EntityType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (a.Columns.Count != b.Columns.Count)
            return false;

        // Column.Type is intentionally not compared here (same as StatisticReconciler): it's a local-only
        // code-generation annotation PlayFab never returns, so it can never actually drift against the remote.
        for (var i = 0; i < a.Columns.Count; i++)
        {
            if (!string.Equals(a.Columns[i].Name, b.Columns[i].Name, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(a.Columns[i].SortDirection, b.Columns[i].SortDirection, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(a.Columns[i].LinkedStatistic ?? "", b.Columns[i].LinkedStatistic ?? "", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(a.Columns[i].LinkedStatisticColumn ?? "", b.Columns[i].LinkedStatisticColumn ?? "", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    internal static bool VersionConfigMatches(LeaderboardDefinitionFile a, LeaderboardDefinitionFile b)
    {
        return string.Equals(a.ResetInterval, b.ResetInterval, StringComparison.OrdinalIgnoreCase)
               && a.MaxQueryableVersions == b.MaxQueryableVersions
               && a.SizeLimit == b.SizeLimit;
    }
}
