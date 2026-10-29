using PlayFab.Cli.Infrastructure;
using PlayFab.Cli.Models;
using Xunit;

namespace PlayFab.Cli.Tests.Infrastructure;

public class LeaderboardReconcilerTests
{
    private static readonly ReconcileOptions DefaultOptions = new(IncludeDeletes: false, AllowRecreate: false);
    private static readonly ReconcileOptions WithDeletes = new(IncludeDeletes: true, AllowRecreate: false);
    private static readonly ReconcileOptions WithRecreate = new(IncludeDeletes: false, AllowRecreate: true);

    // --- Create ---

    [Fact]
    public void Reconcile_LocalOnly_GeneratesCreate()
    {
        var local = new List<LeaderboardDefinitionFile> { MakeDef("TopScores") };
        var remote = new List<LeaderboardDefinitionFile>();

        var result = LeaderboardReconciler.Reconcile(local, remote, DefaultOptions);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.Create, action.Kind);
        Assert.Equal("TopScores", action.LeaderboardName);
        Assert.NotNull(action.LocalDefinition);
        Assert.Null(action.RemoteDefinition);
        Assert.True(result.HasChanges);
    }

    [Fact]
    public void Reconcile_MultipleLocalOnly_GeneratesMultipleCreates()
    {
        var local = new List<LeaderboardDefinitionFile> { MakeDef("Alpha"), MakeDef("Beta") };
        var remote = new List<LeaderboardDefinitionFile>();

        var result = LeaderboardReconciler.Reconcile(local, remote, DefaultOptions);

        Assert.Equal(2, result.Actions.Count);
        Assert.All(result.Actions, a => Assert.Equal(ReconcileActionKind.Create, a.Kind));
    }

    // --- Delete ---

    [Fact]
    public void Reconcile_RemoteOnlyWithoutFlag_WarnsOnly()
    {
        var local = new List<LeaderboardDefinitionFile>();
        var remote = new List<LeaderboardDefinitionFile> { MakeDef("OldBoard") };

        var result = LeaderboardReconciler.Reconcile(local, remote, DefaultOptions);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.Synced, action.Kind);
        Assert.NotNull(action.Warning);
        Assert.Contains("--include-deletes", action.Warning);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void Reconcile_RemoteOnlyWithFlag_GeneratesDelete()
    {
        var local = new List<LeaderboardDefinitionFile>();
        var remote = new List<LeaderboardDefinitionFile> { MakeDef("OldBoard") };

        var result = LeaderboardReconciler.Reconcile(local, remote, WithDeletes);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.Delete, action.Kind);
        Assert.Equal("OldBoard", action.LeaderboardName);
        Assert.True(result.HasChanges);
    }

    // --- Synced ---

    [Fact]
    public void Reconcile_MatchingDefinitions_Synced()
    {
        var local = new List<LeaderboardDefinitionFile> { MakeDef("TopScores") };
        var remote = new List<LeaderboardDefinitionFile> { MakeDef("TopScores") };

        var result = LeaderboardReconciler.Reconcile(local, remote, DefaultOptions);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.Synced, action.Kind);
        Assert.Null(action.Warning);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void Reconcile_NameMatchCaseInsensitive()
    {
        var local = new List<LeaderboardDefinitionFile> { MakeDef("TopScores") };
        var remote = new List<LeaderboardDefinitionFile> { MakeDef("topscores") };

        var result = LeaderboardReconciler.Reconcile(local, remote, DefaultOptions);

        Assert.Equal(ReconcileActionKind.Synced, Assert.Single(result.Actions).Kind);
    }

    // --- Column mismatch ---

    [Fact]
    public void Reconcile_SortDirectionMismatchWithoutFlag_Warns()
    {
        var local = new List<LeaderboardDefinitionFile> { MakeDef("Board", "Descending") };
        var remote = new List<LeaderboardDefinitionFile> { MakeDef("Board", "Ascending") };

        var result = LeaderboardReconciler.Reconcile(local, remote, DefaultOptions);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.VersionConfigDrift, action.Kind);
        Assert.Contains("--allow-recreate", action.Warning);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void Reconcile_SortDirectionMismatchWithFlag_GeneratesRecreate()
    {
        var local = new List<LeaderboardDefinitionFile> { MakeDef("Board", "Descending") };
        var remote = new List<LeaderboardDefinitionFile> { MakeDef("Board", "Ascending") };

        var result = LeaderboardReconciler.Reconcile(local, remote, WithRecreate);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.Recreate, action.Kind);
        Assert.True(result.HasChanges);
    }

    [Fact]
    public void Reconcile_ColumnCountMismatch_DetectedAsDrift()
    {
        var local = new List<LeaderboardDefinitionFile>
        {
            new()
            {
                Name = "Board",
                Columns =
                [
                    new LeaderboardColumnDefinition { Name = "Score", SortDirection = "Descending" },
                    new LeaderboardColumnDefinition { Name = "Rank", SortDirection = "Ascending" },
                ],
            },
        };
        var remote = new List<LeaderboardDefinitionFile> { MakeDef("Board") };

        var result = LeaderboardReconciler.Reconcile(local, remote, DefaultOptions);

        Assert.Equal(ReconcileActionKind.VersionConfigDrift, Assert.Single(result.Actions).Kind);
    }

    [Fact]
    public void Reconcile_ColumnNameMismatch_DetectedAsDrift()
    {
        var local = new List<LeaderboardDefinitionFile>
        {
            new() { Name = "Board", Columns = [new LeaderboardColumnDefinition { Name = "Score", SortDirection = "Descending" }] },
        };
        var remote = new List<LeaderboardDefinitionFile>
        {
            new() { Name = "Board", Columns = [new LeaderboardColumnDefinition { Name = "Points", SortDirection = "Descending" }] },
        };

        var result = LeaderboardReconciler.Reconcile(local, remote, DefaultOptions);

        Assert.Equal(ReconcileActionKind.VersionConfigDrift, result.Actions[0].Kind);
    }

    [Fact]
    public void Reconcile_EntityTypeMismatch_DetectedAsDrift()
    {
        var local = new List<LeaderboardDefinitionFile>
        {
            new() { Name = "Board", EntityType = "title_player_account", Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending" }] },
        };
        var remote = new List<LeaderboardDefinitionFile>
        {
            new() { Name = "Board", EntityType = "group", Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending" }] },
        };

        var result = LeaderboardReconciler.Reconcile(local, remote, DefaultOptions);

        Assert.Equal(ReconcileActionKind.VersionConfigDrift, result.Actions[0].Kind);
    }

    [Fact]
    public void Reconcile_LinkedStatisticMismatch_DetectedAsDrift()
    {
        var local = new List<LeaderboardDefinitionFile>
        {
            new()
            {
                Name = "Board",
                Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending", LinkedStatistic = "StatA", LinkedStatisticColumn = "Value" }],
            },
        };
        var remote = new List<LeaderboardDefinitionFile>
        {
            new()
            {
                Name = "Board",
                Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending", LinkedStatistic = "StatB", LinkedStatisticColumn = "Value" }],
            },
        };

        var result = LeaderboardReconciler.Reconcile(local, remote, DefaultOptions);

        Assert.Equal(ReconcileActionKind.VersionConfigDrift, result.Actions[0].Kind);
    }

    // --- Version config drift ---

    [Fact]
    public void Reconcile_VersionConfigDiffers_WarnsOnly()
    {
        var local = new List<LeaderboardDefinitionFile>
        {
            new() { Name = "Board", ResetInterval = "Month", MaxQueryableVersions = 3, SizeLimit = 2000, Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending" }] },
        };
        var remote = new List<LeaderboardDefinitionFile>
        {
            new() { Name = "Board", ResetInterval = "Manual", MaxQueryableVersions = 1, SizeLimit = 1000, Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending" }] },
        };

        var result = LeaderboardReconciler.Reconcile(local, remote, DefaultOptions);

        var action = Assert.Single(result.Actions);
        Assert.Equal(ReconcileActionKind.VersionConfigDrift, action.Kind);
        Assert.Contains("update-leaderboard not yet supported", action.Warning);
        Assert.False(result.HasChanges);
    }

    // --- Case insensitivity ---

    [Fact]
    public void Reconcile_SortDirectionCaseInsensitive_Synced()
    {
        var local = new List<LeaderboardDefinitionFile> { MakeDef("Board", "descending") };
        var remote = new List<LeaderboardDefinitionFile> { MakeDef("Board", "Descending") };

        var result = LeaderboardReconciler.Reconcile(local, remote, DefaultOptions);

        Assert.Equal(ReconcileActionKind.Synced, result.Actions[0].Kind);
    }

    [Fact]
    public void Reconcile_ColumnNameCaseInsensitive_Synced()
    {
        var local = new List<LeaderboardDefinitionFile>
        {
            new() { Name = "Board", Columns = [new LeaderboardColumnDefinition { Name = "score", SortDirection = "Descending" }] },
        };
        var remote = new List<LeaderboardDefinitionFile>
        {
            new() { Name = "Board", Columns = [new LeaderboardColumnDefinition { Name = "Score", SortDirection = "Descending" }] },
        };

        var result = LeaderboardReconciler.Reconcile(local, remote, DefaultOptions);

        Assert.Equal(ReconcileActionKind.Synced, result.Actions[0].Kind);
    }

    // --- Sorting ---

    [Fact]
    public void Reconcile_ResultsSortedByName()
    {
        var local = new List<LeaderboardDefinitionFile> { MakeDef("Zebra"), MakeDef("Alpha") };
        var remote = new List<LeaderboardDefinitionFile>();

        var result = LeaderboardReconciler.Reconcile(local, remote, DefaultOptions);

        Assert.Equal("Alpha", result.Actions[0].LeaderboardName);
        Assert.Equal("Zebra", result.Actions[1].LeaderboardName);
    }

    // --- Mixed scenarios ---

    [Fact]
    public void Reconcile_MixedScenario_CorrectActions()
    {
        var local = new List<LeaderboardDefinitionFile>
        {
            MakeDef("Existing"),
            MakeDef("NewBoard"),
        };
        var remote = new List<LeaderboardDefinitionFile>
        {
            MakeDef("Existing"),
            MakeDef("OldBoard"),
        };

        var result = LeaderboardReconciler.Reconcile(local, remote, WithDeletes);

        Assert.Equal(3, result.Actions.Count);
        Assert.Equal(ReconcileActionKind.Synced, result.Actions.First(a => a.LeaderboardName == "Existing").Kind);
        Assert.Equal(ReconcileActionKind.Create, result.Actions.First(a => a.LeaderboardName == "NewBoard").Kind);
        Assert.Equal(ReconcileActionKind.Delete, result.Actions.First(a => a.LeaderboardName == "OldBoard").Kind);
    }

    // --- HasChanges ---

    [Fact]
    public void HasChanges_NoActions_False()
    {
        var result = new LeaderboardReconcileResult([]);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void HasChanges_OnlySynced_False()
    {
        var result = new LeaderboardReconcileResult([new LeaderboardReconcileAction(ReconcileActionKind.Synced, "B", null, null, null)]);
        Assert.False(result.HasChanges);
    }

    [Theory]
    [InlineData(ReconcileActionKind.Create)]
    [InlineData(ReconcileActionKind.Delete)]
    [InlineData(ReconcileActionKind.Recreate)]
    public void HasChanges_ActionableKind_True(ReconcileActionKind kind)
    {
        var result = new LeaderboardReconcileResult([new LeaderboardReconcileAction(kind, "B", null, null, null)]);
        Assert.True(result.HasChanges);
    }

    // --- DefinitionsMatch ---

    [Fact]
    public void DefinitionsMatch_Identical_True()
    {
        Assert.True(LeaderboardReconciler.DefinitionsMatch(MakeDef("B"), MakeDef("B")));
    }

    [Fact]
    public void DefinitionsMatch_DifferentSortDirection_False()
    {
        Assert.False(LeaderboardReconciler.DefinitionsMatch(MakeDef("B", "Descending"), MakeDef("B", "Ascending")));
    }

    [Fact]
    public void DefinitionsMatch_IgnoresVersionConfig_True()
    {
        var a = new LeaderboardDefinitionFile { Name = "B", ResetInterval = "Month", MaxQueryableVersions = 3, SizeLimit = 2000, Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending" }] };
        var b = new LeaderboardDefinitionFile { Name = "B", ResetInterval = "Manual", MaxQueryableVersions = 1, SizeLimit = 1000, Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending" }] };

        Assert.True(LeaderboardReconciler.DefinitionsMatch(a, b));
    }

    [Fact]
    public void DefinitionsMatch_LinkedStatisticDifference_False()
    {
        var a = new LeaderboardDefinitionFile
        {
            Name = "B",
            Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending", LinkedStatistic = "StatA", LinkedStatisticColumn = "Value" }],
        };
        var b = new LeaderboardDefinitionFile
        {
            Name = "B",
            Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending" }],
        };

        Assert.False(LeaderboardReconciler.DefinitionsMatch(a, b));
    }

    // --- VersionConfigMatches ---

    [Fact]
    public void VersionConfigMatches_SizeLimitDiffers_False()
    {
        var a = new LeaderboardDefinitionFile { Name = "B", SizeLimit = 1000, Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending" }] };
        var b = new LeaderboardDefinitionFile { Name = "B", SizeLimit = 2000, Columns = [new LeaderboardColumnDefinition { Name = "V", SortDirection = "Descending" }] };

        Assert.False(LeaderboardReconciler.VersionConfigMatches(a, b));
    }

    // --- helpers ---

    private static LeaderboardDefinitionFile MakeDef(string name, string sortDirection = "Descending")
    {
        return new LeaderboardDefinitionFile
        {
            Name = name,
            Columns = [new LeaderboardColumnDefinition { Name = "Score", SortDirection = sortDirection }],
        };
    }
}
