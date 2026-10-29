using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;
using Xunit;

namespace PlayFab.Cli.Tests.Infrastructure;

public class StatisticReconcilerTests
{
    private static readonly ReconcileOptions DefaultOptions = new(IncludeDeletes: false, AllowRecreate: false);
    private static readonly ReconcileOptions WithDeletes = new(IncludeDeletes: true, AllowRecreate: false);
    private static readonly ReconcileOptions WithRecreate = new(IncludeDeletes: false, AllowRecreate: true);
    private static readonly ReconcileOptions WithAll = new(IncludeDeletes: true, AllowRecreate: true);

    // --- Create ---

    [Fact]
    public void Reconcile_LocalOnly_GeneratesCreate()
    {
        var local = new List<StatisticDefinitionFile> { MakeDef("PlayerScore") };
        var remote = new List<StatisticDefinitionFile>();

        var result = StatisticReconciler.Reconcile(local, remote, DefaultOptions);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.Create, action.Kind);
        Assert.Equal("PlayerScore", action.StatisticName);
        Assert.NotNull(action.LocalDefinition);
        Assert.Null(action.RemoteDefinition);
        Assert.True(result.HasChanges);
    }

    [Fact]
    public void Reconcile_MultipleLocalOnly_GeneratesMultipleCreates()
    {
        var local = new List<StatisticDefinitionFile> { MakeDef("Alpha"), MakeDef("Beta") };
        var remote = new List<StatisticDefinitionFile>();

        var result = StatisticReconciler.Reconcile(local, remote, DefaultOptions);

        Assert.Equal(2, result.Actions.Count);
        Assert.All(result.Actions, a => Assert.Equal(ReconcileActionKind.Create, a.Kind));
    }

    // --- Delete ---

    [Fact]
    public void Reconcile_RemoteOnlyWithoutFlag_WarnsOnly()
    {
        var local = new List<StatisticDefinitionFile>();
        var remote = new List<StatisticDefinitionFile> { MakeDef("OldStat") };

        var result = StatisticReconciler.Reconcile(local, remote, DefaultOptions);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.Synced, action.Kind);
        Assert.NotNull(action.Warning);
        Assert.Contains("--include-deletes", action.Warning);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void Reconcile_RemoteOnlyWithFlag_GeneratesDelete()
    {
        var local = new List<StatisticDefinitionFile>();
        var remote = new List<StatisticDefinitionFile> { MakeDef("OldStat") };

        var result = StatisticReconciler.Reconcile(local, remote, WithDeletes);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.Delete, action.Kind);
        Assert.Equal("OldStat", action.StatisticName);
        Assert.Null(action.LocalDefinition);
        Assert.NotNull(action.RemoteDefinition);
        Assert.True(result.HasChanges);
    }

    // --- Synced ---

    [Fact]
    public void Reconcile_MatchingDefinitions_Synced()
    {
        var local = new List<StatisticDefinitionFile> { MakeDef("PlayerScore") };
        var remote = new List<StatisticDefinitionFile> { MakeDef("PlayerScore") };

        var result = StatisticReconciler.Reconcile(local, remote, DefaultOptions);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.Synced, action.Kind);
        Assert.Null(action.Warning);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void Reconcile_NameMatchCaseInsensitive()
    {
        var local = new List<StatisticDefinitionFile> { MakeDef("PlayerScore") };
        var remote = new List<StatisticDefinitionFile> { MakeDef("playerscore") };

        var result = StatisticReconciler.Reconcile(local, remote, DefaultOptions);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.Synced, action.Kind);
    }

    // --- Column mismatch ---

    [Fact]
    public void Reconcile_ColumnMismatchWithoutFlag_Warns()
    {
        var local = new List<StatisticDefinitionFile> { MakeDef("PlayerScore", "Sum") };
        var remote = new List<StatisticDefinitionFile> { MakeDef("PlayerScore", "Max") };

        var result = StatisticReconciler.Reconcile(local, remote, DefaultOptions);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.VersionConfigDrift, action.Kind);
        Assert.NotNull(action.Warning);
        Assert.Contains("--allow-recreate", action.Warning);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void Reconcile_ColumnMismatchWithFlag_GeneratesRecreate()
    {
        var local = new List<StatisticDefinitionFile> { MakeDef("PlayerScore", "Sum") };
        var remote = new List<StatisticDefinitionFile> { MakeDef("PlayerScore", "Max") };

        var result = StatisticReconciler.Reconcile(local, remote, WithRecreate);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.Recreate, action.Kind);
        Assert.NotNull(action.LocalDefinition);
        Assert.NotNull(action.RemoteDefinition);
        Assert.True(result.HasChanges);
    }

    [Fact]
    public void Reconcile_ColumnCountMismatch_DetectedAsDrift()
    {
        var local = new List<StatisticDefinitionFile>
        {
            new()
            {
                Name = "RaceResult",
                Columns =
                [
                    new StatisticColumnDefinition { Name = "Time", AggregationMethod = "Min" },
                    new StatisticColumnDefinition { Name = "Distance", AggregationMethod = "Max" },
                ],
            },
        };
        var remote = new List<StatisticDefinitionFile> { MakeDef("RaceResult", "Min") };

        var result = StatisticReconciler.Reconcile(local, remote, DefaultOptions);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.VersionConfigDrift, action.Kind);
        Assert.Contains("--allow-recreate", action.Warning);
    }

    [Fact]
    public void Reconcile_ColumnNameMismatch_DetectedAsDrift()
    {
        var local = new List<StatisticDefinitionFile>
        {
            new() { Name = "Stat", Columns = [new StatisticColumnDefinition { Name = "Score", AggregationMethod = "Sum" }] },
        };
        var remote = new List<StatisticDefinitionFile>
        {
            new() { Name = "Stat", Columns = [new StatisticColumnDefinition { Name = "Points", AggregationMethod = "Sum" }] },
        };

        var result = StatisticReconciler.Reconcile(local, remote, DefaultOptions);

        Assert.Equal(ReconcileActionKind.VersionConfigDrift, result.Actions[0].Kind);
    }

    [Fact]
    public void Reconcile_EntityTypeMismatch_DetectedAsDrift()
    {
        var local = new List<StatisticDefinitionFile>
        {
            new() { Name = "Stat", EntityType = "title_player_account", Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" }] },
        };
        var remote = new List<StatisticDefinitionFile>
        {
            new() { Name = "Stat", EntityType = "group", Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" }] },
        };

        var result = StatisticReconciler.Reconcile(local, remote, DefaultOptions);

        Assert.Equal(ReconcileActionKind.VersionConfigDrift, result.Actions[0].Kind);
    }

    // --- Version config drift ---

    [Fact]
    public void Reconcile_VersionConfigDiffers_WarnsOnly()
    {
        var local = new List<StatisticDefinitionFile>
        {
            new() { Name = "Stat", ResetInterval = "Month", MaxQueryableVersions = 3, Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" }] },
        };
        var remote = new List<StatisticDefinitionFile>
        {
            new() { Name = "Stat", ResetInterval = "Manual", MaxQueryableVersions = 1, Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" }] },
        };

        var result = StatisticReconciler.Reconcile(local, remote, DefaultOptions);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.VersionConfigDrift, action.Kind);
        Assert.Contains("update-statistic not yet supported", action.Warning);
        Assert.False(result.HasChanges);
    }

    // --- Case insensitivity ---

    [Fact]
    public void Reconcile_AggregationMethodCaseInsensitive_Synced()
    {
        var local = new List<StatisticDefinitionFile> { MakeDef("Stat", "sum") };
        var remote = new List<StatisticDefinitionFile> { MakeDef("Stat", "Sum") };

        var result = StatisticReconciler.Reconcile(local, remote, DefaultOptions);

        Assert.Equal(ReconcileActionKind.Synced, result.Actions[0].Kind);
    }

    [Fact]
    public void Reconcile_ColumnNameCaseInsensitive_Synced()
    {
        var local = new List<StatisticDefinitionFile>
        {
            new() { Name = "Stat", Columns = [new StatisticColumnDefinition { Name = "value", AggregationMethod = "Sum" }] },
        };
        var remote = new List<StatisticDefinitionFile>
        {
            new() { Name = "Stat", Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum" }] },
        };

        var result = StatisticReconciler.Reconcile(local, remote, DefaultOptions);

        Assert.Equal(ReconcileActionKind.Synced, result.Actions[0].Kind);
    }

    // --- Sorting ---

    [Fact]
    public void Reconcile_ResultsSortedByName()
    {
        var local = new List<StatisticDefinitionFile> { MakeDef("Zebra"), MakeDef("Alpha") };
        var remote = new List<StatisticDefinitionFile>();

        var result = StatisticReconciler.Reconcile(local, remote, DefaultOptions);

        Assert.Equal("Alpha", result.Actions[0].StatisticName);
        Assert.Equal("Zebra", result.Actions[1].StatisticName);
    }

    // --- Mixed scenarios ---

    [Fact]
    public void Reconcile_MixedScenario_CorrectActions()
    {
        var local = new List<StatisticDefinitionFile>
        {
            MakeDef("Existing"),
            MakeDef("NewStat"),
        };
        var remote = new List<StatisticDefinitionFile>
        {
            MakeDef("Existing"),
            MakeDef("OldStat"),
        };

        var result = StatisticReconciler.Reconcile(local, remote, WithDeletes);

        Assert.Equal(3, result.Actions.Count);
        Assert.Equal(ReconcileActionKind.Synced, result.Actions.First(a => a.StatisticName == "Existing").Kind);
        Assert.Equal(ReconcileActionKind.Create, result.Actions.First(a => a.StatisticName == "NewStat").Kind);
        Assert.Equal(ReconcileActionKind.Delete, result.Actions.First(a => a.StatisticName == "OldStat").Kind);
    }

    // --- HasChanges ---

    [Fact]
    public void HasChanges_NoActions_False()
    {
        var result = new ReconcileResult([]);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void HasChanges_OnlySynced_False()
    {
        var result = new ReconcileResult([new ReconcileAction(ReconcileActionKind.Synced, "Stat", null, null, null)]);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void HasChanges_OnlyWarnings_False()
    {
        var result = new ReconcileResult([new ReconcileAction(ReconcileActionKind.VersionConfigDrift, "Stat", null, null, "warn")]);
        Assert.False(result.HasChanges);
    }

    [Theory]
    [InlineData(ReconcileActionKind.Create)]
    [InlineData(ReconcileActionKind.Delete)]
    [InlineData(ReconcileActionKind.Recreate)]
    public void HasChanges_ActionableKind_True(ReconcileActionKind kind)
    {
        var result = new ReconcileResult([new ReconcileAction(kind, "Stat", null, null, null)]);
        Assert.True(result.HasChanges);
    }

    // --- DefinitionsMatch ---

    [Fact]
    public void DefinitionsMatch_Identical_True()
    {
        Assert.True(StatisticReconciler.DefinitionsMatch(MakeDef("S"), MakeDef("S")));
    }

    [Fact]
    public void DefinitionsMatch_DifferentAggregation_False()
    {
        Assert.False(StatisticReconciler.DefinitionsMatch(MakeDef("S", "Sum"), MakeDef("S", "Max")));
    }

    [Fact]
    public void DefinitionsMatch_IgnoresType_True()
    {
        var a = new StatisticDefinitionFile
        {
            Name = "S",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum", Type = "number" }],
        };
        var b = new StatisticDefinitionFile
        {
            Name = "S",
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = "Sum", Type = "string" }],
        };

        Assert.True(StatisticReconciler.DefinitionsMatch(a, b));
    }

    [Fact]
    public void DefinitionsMatch_IgnoresVersionConfig_True()
    {
        var a = new StatisticDefinitionFile { Name = "S", ResetInterval = "Month", MaxQueryableVersions = 3, Columns = [new StatisticColumnDefinition { Name = "V", AggregationMethod = "Sum" }] };
        var b = new StatisticDefinitionFile { Name = "S", ResetInterval = "Manual", MaxQueryableVersions = 1, Columns = [new StatisticColumnDefinition { Name = "V", AggregationMethod = "Sum" }] };

        Assert.True(StatisticReconciler.DefinitionsMatch(a, b));
    }

    // --- helpers ---

    private static StatisticDefinitionFile MakeDef(string name, string aggregation = "Sum")
    {
        return new StatisticDefinitionFile
        {
            Name = name,
            Columns = [new StatisticColumnDefinition { Name = "Value", AggregationMethod = aggregation }],
        };
    }
}
