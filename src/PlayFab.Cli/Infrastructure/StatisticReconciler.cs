using PlayFab.Cli.Models;

namespace PlayFab.Cli.Infrastructure;

public enum ReconcileActionKind { Create, Delete, Recreate, VersionConfigDrift, Synced }

public sealed record ReconcileAction(
    ReconcileActionKind Kind,
    string StatisticName,
    StatisticDefinitionFile? LocalDefinition,
    StatisticDefinitionFile? RemoteDefinition,
    string? Warning);

public sealed record ReconcileOptions(bool IncludeDeletes, bool AllowRecreate);

public sealed record ReconcileResult(List<ReconcileAction> Actions)
{
    public bool HasChanges => Actions.Any(a => a.Kind is ReconcileActionKind.Create
        or ReconcileActionKind.Delete or ReconcileActionKind.Recreate);
}

public static class StatisticReconciler
{
    public static ReconcileResult Reconcile(
        List<StatisticDefinitionFile> local,
        List<StatisticDefinitionFile> remote,
        ReconcileOptions options)
    {
        var actions = new List<ReconcileAction>();

        var remoteByName = remote.ToDictionary(d => d.Name, d => d, StringComparer.OrdinalIgnoreCase);
        var localByName = local.ToDictionary(d => d.Name, d => d, StringComparer.OrdinalIgnoreCase);

        foreach (var localDef in local)
        {
            if (!remoteByName.TryGetValue(localDef.Name, out var remoteDef))
            {
                actions.Add(new ReconcileAction(ReconcileActionKind.Create, localDef.Name, localDef, null, null));
                continue;
            }

            if (DefinitionsMatch(localDef, remoteDef))
            {
                if (!VersionConfigMatches(localDef, remoteDef))
                {
                    actions.Add(new ReconcileAction(ReconcileActionKind.VersionConfigDrift, localDef.Name, localDef, remoteDef,
                        $"Version config differs for '{localDef.Name}' (resetInterval/maxQueryableVersions) — update-statistic not yet supported."));
                }
                else
                {
                    actions.Add(new ReconcileAction(ReconcileActionKind.Synced, localDef.Name, localDef, remoteDef, null));
                }
                continue;
            }

            if (options.AllowRecreate)
            {
                actions.Add(new ReconcileAction(ReconcileActionKind.Recreate, localDef.Name, localDef, remoteDef, null));
            }
            else
            {
                actions.Add(new ReconcileAction(ReconcileActionKind.VersionConfigDrift, localDef.Name, localDef, remoteDef,
                    $"Column mismatch for '{localDef.Name}' — use --allow-recreate to generate delete+create steps (destructive: loses player data)."));
            }
        }

        foreach (var remoteDef in remote)
        {
            if (localByName.ContainsKey(remoteDef.Name))
                continue;

            if (options.IncludeDeletes)
            {
                actions.Add(new ReconcileAction(ReconcileActionKind.Delete, remoteDef.Name, null, remoteDef, null));
            }
            else
            {
                actions.Add(new ReconcileAction(ReconcileActionKind.Synced, remoteDef.Name, null, remoteDef,
                    $"Statistic '{remoteDef.Name}' exists on PlayFab but has no local definition — use --include-deletes to generate a delete step."));
            }
        }

        return new ReconcileResult(actions.OrderBy(a => a.StatisticName, StringComparer.OrdinalIgnoreCase).ToList());
    }

    internal static bool DefinitionsMatch(StatisticDefinitionFile a, StatisticDefinitionFile b)
    {
        if (!string.Equals(a.EntityType, b.EntityType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (a.Columns.Count != b.Columns.Count)
            return false;

        for (var i = 0; i < a.Columns.Count; i++)
        {
            if (!string.Equals(a.Columns[i].Name, b.Columns[i].Name, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(a.Columns[i].AggregationMethod, b.Columns[i].AggregationMethod, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    internal static bool VersionConfigMatches(StatisticDefinitionFile a, StatisticDefinitionFile b)
    {
        return string.Equals(a.ResetInterval, b.ResetInterval, StringComparison.OrdinalIgnoreCase)
               && a.MaxQueryableVersions == b.MaxQueryableVersions;
    }
}
