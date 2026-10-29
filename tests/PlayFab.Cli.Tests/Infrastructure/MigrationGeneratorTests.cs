using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;
using Xunit;

namespace PlayFab.Cli.Tests.Infrastructure;

public class MigrationGeneratorTests
{
    [Fact]
    public void Generate_CreateAction_ProducesCreateUpDeleteDown()
    {
        var localDef = MakeDef("PlayerScore", "Sum");
        var result = new ReconcileResult(
        [
            new ReconcileAction(ReconcileActionKind.Create, "PlayerScore", localDef, null, null),
        ]);

        var migration = MigrationGenerator.GenerateStatisticMigration(result, "test");

        Assert.Single(migration.Up);
        Assert.Equal(OperationTypes.CreateStatistic, migration.Up[0].Type);
        Assert.Equal("PlayerScore", migration.Up[0].Name);
        Assert.NotNull(migration.Up[0].Columns);
        Assert.Single(migration.Up[0].Columns!);
        Assert.Equal("Value", migration.Up[0].Columns![0].Name);
        Assert.Equal("Sum", migration.Up[0].Columns![0].AggregationMethod);

        Assert.Single(migration.Down);
        Assert.Equal(OperationTypes.DeleteStatistic, migration.Down[0].Type);
        Assert.Equal("PlayerScore", migration.Down[0].Name);
    }

    [Fact]
    public void Generate_DeleteAction_ProducesDeleteUpCreateDown()
    {
        var remoteDef = MakeDef("OldStat", "Max");
        var result = new ReconcileResult(
        [
            new ReconcileAction(ReconcileActionKind.Delete, "OldStat", null, remoteDef, null),
        ]);

        var migration = MigrationGenerator.GenerateStatisticMigration(result, "test");

        Assert.Single(migration.Up);
        Assert.Equal(OperationTypes.DeleteStatistic, migration.Up[0].Type);
        Assert.Equal("OldStat", migration.Up[0].Name);

        Assert.Single(migration.Down);
        Assert.Equal(OperationTypes.CreateStatistic, migration.Down[0].Type);
        Assert.Equal("OldStat", migration.Down[0].Name);
        Assert.Equal("Max", migration.Down[0].Columns![0].AggregationMethod);
    }

    [Fact]
    public void Generate_RecreateAction_ProducesDeleteThenCreateUpAndReverse()
    {
        var localDef = MakeDef("PlayerScore", "Sum");
        var remoteDef = MakeDef("PlayerScore", "Max");
        var result = new ReconcileResult(
        [
            new ReconcileAction(ReconcileActionKind.Recreate, "PlayerScore", localDef, remoteDef, null),
        ]);

        var migration = MigrationGenerator.GenerateStatisticMigration(result, "test");

        Assert.Equal(2, migration.Up.Count);
        Assert.Equal(OperationTypes.DeleteStatistic, migration.Up[0].Type);
        Assert.Equal("PlayerScore", migration.Up[0].Name);
        Assert.Equal(OperationTypes.CreateStatistic, migration.Up[1].Type);
        Assert.Equal("Sum", migration.Up[1].Columns![0].AggregationMethod);

        Assert.Equal(2, migration.Down.Count);
        Assert.Equal(OperationTypes.DeleteStatistic, migration.Down[0].Type);
        Assert.Equal("PlayerScore", migration.Down[0].Name);
        Assert.Equal(OperationTypes.CreateStatistic, migration.Down[1].Type);
        Assert.Equal("Max", migration.Down[1].Columns![0].AggregationMethod);
    }

    [Fact]
    public void Generate_SyncedAndWarnings_Skipped()
    {
        var result = new ReconcileResult(
        [
            new ReconcileAction(ReconcileActionKind.Synced, "Synced", null, null, null),
            new ReconcileAction(ReconcileActionKind.VersionConfigDrift, "Drift", null, null, "warning"),
        ]);

        var migration = MigrationGenerator.GenerateStatisticMigration(result, "test");

        Assert.Empty(migration.Up);
        Assert.Empty(migration.Down);
    }

    [Fact]
    public void Generate_PreservesDescription()
    {
        var result = new ReconcileResult(
        [
            new ReconcileAction(ReconcileActionKind.Create, "S", MakeDef("S"), null, null),
        ]);

        var migration = MigrationGenerator.GenerateStatisticMigration(result, "my description");

        Assert.Equal("my description", migration.Description);
    }

    [Fact]
    public void Generate_CreateMapsAllDefinitionFields()
    {
        var def = new StatisticDefinitionFile
        {
            Name = "RaceResult",
            EntityType = "group",
            ResetInterval = "Week",
            MaxQueryableVersions = 5,
            Columns =
            [
                new StatisticColumnDefinition { Name = "Time", AggregationMethod = "Min" },
                new StatisticColumnDefinition { Name = "Distance", AggregationMethod = "Max" },
            ],
        };

        var result = new ReconcileResult(
        [
            new ReconcileAction(ReconcileActionKind.Create, "RaceResult", def, null, null),
        ]);

        var migration = MigrationGenerator.GenerateStatisticMigration(result, "test");
        var step = migration.Up[0];

        Assert.Equal("RaceResult", step.Name);
        Assert.Equal("group", step.EntityType);
        Assert.Equal("Week", step.ResetInterval);
        Assert.Equal(5, step.MaxQueryableVersions);
        Assert.Equal(2, step.Columns!.Count);
        Assert.Equal("Time", step.Columns[0].Name);
        Assert.Equal("Min", step.Columns[0].AggregationMethod);
        Assert.Equal("Distance", step.Columns[1].Name);
        Assert.Equal("Max", step.Columns[1].AggregationMethod);
    }

    [Fact]
    public void Generate_MultipleActions_AllIncluded()
    {
        var result = new ReconcileResult(
        [
            new ReconcileAction(ReconcileActionKind.Create, "New1", MakeDef("New1"), null, null),
            new ReconcileAction(ReconcileActionKind.Create, "New2", MakeDef("New2"), null, null),
            new ReconcileAction(ReconcileActionKind.Delete, "Old1", null, MakeDef("Old1"), null),
        ]);

        var migration = MigrationGenerator.GenerateStatisticMigration(result, "test");

        Assert.Equal(3, migration.Up.Count);
        Assert.Equal(3, migration.Down.Count);
    }

    private static StatisticDefinitionFile MakeDef(string name, string aggregation = "Sum")
    {
        return new StatisticDefinitionFile
        {
            Name = name,
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = aggregation }],
        };
    }
}
