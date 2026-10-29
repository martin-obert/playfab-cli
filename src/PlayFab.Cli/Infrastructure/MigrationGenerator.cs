using PlayFab.Cli.Models;

namespace PlayFab.Cli.Infrastructure;

public static class MigrationGenerator
{
    public static MigrationDefinition GenerateStatisticMigration(ReconcileResult result, string description)
    {
        var upSteps = new List<MigrationStep>();
        var downSteps = new List<MigrationStep>();

        foreach (var action in result.Actions)
        {
            switch (action.Kind)
            {
                case ReconcileActionKind.Create:
                    upSteps.Add(CreateStatisticStep(action.LocalDefinition!));
                    downSteps.Add(DeleteStatisticStep(action.LocalDefinition!.Name));
                    break;

                case ReconcileActionKind.Delete:
                    upSteps.Add(DeleteStatisticStep(action.RemoteDefinition!.Name));
                    downSteps.Add(CreateStatisticStep(action.RemoteDefinition!));
                    break;

                case ReconcileActionKind.Recreate:
                    upSteps.Add(DeleteStatisticStep(action.RemoteDefinition!.Name));
                    upSteps.Add(CreateStatisticStep(action.LocalDefinition!));
                    downSteps.Add(DeleteStatisticStep(action.LocalDefinition!.Name));
                    downSteps.Add(CreateStatisticStep(action.RemoteDefinition!));
                    break;
            }
        }

        return new MigrationDefinition
        {
            Description = description,
            Up = upSteps,
            Down = downSteps,
        };
    }

    private static MigrationStep CreateStatisticStep(StatisticDefinitionFile definition)
    {
        return new MigrationStep
        {
            Type = OperationTypes.CreateStatistic,
            Name = definition.Name,
            EntityType = definition.EntityType,
            ResetInterval = definition.ResetInterval,
            MaxQueryableVersions = definition.MaxQueryableVersions,
            Columns = definition.Columns.Select(c => new StatisticColumnStep
            {
                Name = c.Name,
                AggregationMethod = c.AggregationMethod,
            }).ToList(),
        };
    }

    private static MigrationStep DeleteStatisticStep(string name)
    {
        return new MigrationStep
        {
            Type = OperationTypes.DeleteStatistic,
            Name = name,
        };
    }
}
