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

    // === Leaderboard generation ===

    [Fact]
    public void GenerateLeaderboard_CreateAction_ProducesCreateUpDeleteDown()
    {
        var localDef = MakeLbDef("TopScores");
        var result = new LeaderboardReconcileResult(
        [
            new LeaderboardReconcileAction(ReconcileActionKind.Create, "TopScores", localDef, null, null),
        ]);

        var migration = MigrationGenerator.GenerateLeaderboardMigration(result, "test");

        Assert.Single(migration.Up);
        Assert.Equal(OperationTypes.CreateLeaderboard, migration.Up[0].Type);
        Assert.Equal("TopScores", migration.Up[0].Name);
        Assert.NotNull(migration.Up[0].LeaderboardColumns);
        Assert.Single(migration.Up[0].LeaderboardColumns!);
        Assert.Equal("Score", migration.Up[0].LeaderboardColumns![0].Name);
        Assert.Equal("Descending", migration.Up[0].LeaderboardColumns![0].SortDirection);

        Assert.Single(migration.Down);
        Assert.Equal(OperationTypes.DeleteLeaderboard, migration.Down[0].Type);
        Assert.Equal("TopScores", migration.Down[0].Name);
    }

    [Fact]
    public void GenerateLeaderboard_DeleteAction_ProducesDeleteUpCreateDown()
    {
        var remoteDef = MakeLbDef("OldBoard", "Ascending");
        var result = new LeaderboardReconcileResult(
        [
            new LeaderboardReconcileAction(ReconcileActionKind.Delete, "OldBoard", null, remoteDef, null),
        ]);

        var migration = MigrationGenerator.GenerateLeaderboardMigration(result, "test");

        Assert.Single(migration.Up);
        Assert.Equal(OperationTypes.DeleteLeaderboard, migration.Up[0].Type);

        Assert.Single(migration.Down);
        Assert.Equal(OperationTypes.CreateLeaderboard, migration.Down[0].Type);
        Assert.Equal("Ascending", migration.Down[0].LeaderboardColumns![0].SortDirection);
    }

    [Fact]
    public void GenerateLeaderboard_RecreateAction_ProducesDeleteThenCreateAndReverse()
    {
        var localDef = MakeLbDef("Board", "Descending");
        var remoteDef = MakeLbDef("Board", "Ascending");
        var result = new LeaderboardReconcileResult(
        [
            new LeaderboardReconcileAction(ReconcileActionKind.Recreate, "Board", localDef, remoteDef, null),
        ]);

        var migration = MigrationGenerator.GenerateLeaderboardMigration(result, "test");

        Assert.Equal(2, migration.Up.Count);
        Assert.Equal(OperationTypes.DeleteLeaderboard, migration.Up[0].Type);
        Assert.Equal(OperationTypes.CreateLeaderboard, migration.Up[1].Type);
        Assert.Equal("Descending", migration.Up[1].LeaderboardColumns![0].SortDirection);

        Assert.Equal(2, migration.Down.Count);
        Assert.Equal(OperationTypes.DeleteLeaderboard, migration.Down[0].Type);
        Assert.Equal(OperationTypes.CreateLeaderboard, migration.Down[1].Type);
        Assert.Equal("Ascending", migration.Down[1].LeaderboardColumns![0].SortDirection);
    }

    [Fact]
    public void GenerateLeaderboard_SyncedAndWarnings_Skipped()
    {
        var result = new LeaderboardReconcileResult(
        [
            new LeaderboardReconcileAction(ReconcileActionKind.Synced, "Synced", null, null, null),
            new LeaderboardReconcileAction(ReconcileActionKind.VersionConfigDrift, "Drift", null, null, "warning"),
        ]);

        var migration = MigrationGenerator.GenerateLeaderboardMigration(result, "test");

        Assert.Empty(migration.Up);
        Assert.Empty(migration.Down);
    }

    [Fact]
    public void GenerateLeaderboard_CreateMapsAllDefinitionFields()
    {
        var def = new LeaderboardDefinitionFile
        {
            Name = "RaceResults",
            EntityType = "group",
            ResetInterval = "Week",
            MaxQueryableVersions = 5,
            SizeLimit = 2000,
            Columns =
            [
                new LeaderboardColumnDefinition
                {
                    Name = "Time",
                    SortDirection = "Ascending",
                    LinkedStatistic = "RaceTimes",
                    LinkedStatisticColumn = "BestTime",
                },
                new LeaderboardColumnDefinition { Name = "Laps", SortDirection = "Descending" },
            ],
        };

        var result = new LeaderboardReconcileResult(
        [
            new LeaderboardReconcileAction(ReconcileActionKind.Create, "RaceResults", def, null, null),
        ]);

        var migration = MigrationGenerator.GenerateLeaderboardMigration(result, "test");
        var step = migration.Up[0];

        Assert.Equal("RaceResults", step.Name);
        Assert.Equal("group", step.EntityType);
        Assert.Equal("Week", step.ResetInterval);
        Assert.Equal(5, step.MaxQueryableVersions);
        Assert.Equal(2000, step.SizeLimit);
        Assert.Equal(2, step.LeaderboardColumns!.Count);
        Assert.Equal("Time", step.LeaderboardColumns[0].Name);
        Assert.Equal("Ascending", step.LeaderboardColumns[0].SortDirection);
        Assert.Equal("RaceTimes", step.LeaderboardColumns[0].LinkedStatistic);
        Assert.Equal("BestTime", step.LeaderboardColumns[0].LinkedStatisticColumn);
        Assert.Equal("Laps", step.LeaderboardColumns[1].Name);
        Assert.Null(step.LeaderboardColumns[1].LinkedStatistic);
    }

    [Fact]
    public void GenerateLeaderboard_MultipleActions_AllIncluded()
    {
        var result = new LeaderboardReconcileResult(
        [
            new LeaderboardReconcileAction(ReconcileActionKind.Create, "New1", MakeLbDef("New1"), null, null),
            new LeaderboardReconcileAction(ReconcileActionKind.Create, "New2", MakeLbDef("New2"), null, null),
            new LeaderboardReconcileAction(ReconcileActionKind.Delete, "Old1", null, MakeLbDef("Old1"), null),
        ]);

        var migration = MigrationGenerator.GenerateLeaderboardMigration(result, "test");

        Assert.Equal(3, migration.Up.Count);
        Assert.Equal(3, migration.Down.Count);
    }

    private static LeaderboardDefinitionFile MakeLbDef(string name, string sortDirection = "Descending")
    {
        return new LeaderboardDefinitionFile
        {
            Name = name,
            Columns = [new LeaderboardColumnDefinition { Name = "Score", SortDirection = sortDirection }],
        };
    }
}
