using PlayFab;
using PlayFab.AdminModels;
using PlayFab.ProgressionModels;
using PlayFab.Cli.Models;

namespace PlayFab.Cli.Infrastructure;

public static class MigrationRunner
{
    public static async Task<MigrationRunResult> ApplyAsync(MigrationFile migration, MigrationLedger ledger)
    {
        var validationErrors = MigrationLoader.Validate(migration);
        if (validationErrors.Count > 0)
            return new MigrationRunResult(false, $"Validation failed: {string.Join("; ", validationErrors)}", 0);

        var stepsExecuted = 0;
        foreach (var step in migration.Definition.Up)
        {
            try
            {
                await ExecuteStepAsync(step);
                stepsExecuted++;
            }
            catch (Exception ex)
            {
                return new MigrationRunResult(false, $"Step {stepsExecuted} ({step.Type} {StepTarget(step)}) failed: {ex.Message}", stepsExecuted);
            }
        }

        ledger.AppliedMigrations.Add(new MigrationRecord
        {
            Id = migration.Id,
            Name = migration.Name,
            Description = migration.Definition.Description,
            AppliedAt = DateTime.UtcNow,
        });
        await MigrationLedgerService.WriteLedgerAsync(ledger);

        return new MigrationRunResult(true, null, stepsExecuted);
    }

    public static async Task<MigrationRunResult> RollbackAsync(MigrationFile migration, MigrationLedger ledger)
    {
        var stepsExecuted = 0;
        foreach (var step in migration.Definition.Down)
        {
            try
            {
                await ExecuteStepAsync(step);
                stepsExecuted++;
            }
            catch (Exception ex)
            {
                return new MigrationRunResult(false, $"Step {stepsExecuted} ({step.Type} {StepTarget(step)}) failed: {ex.Message}", stepsExecuted);
            }
        }

        ledger.AppliedMigrations.RemoveAll(r => r.Id == migration.Id);
        await MigrationLedgerService.WriteLedgerAsync(ledger);

        return new MigrationRunResult(true, null, stepsExecuted);
    }

    private static async Task ExecuteStepAsync(MigrationStep step)
    {
        if (!OperationTypes.All.Contains(step.Type))
            throw new InvalidOperationException($"Unknown operation type '{step.Type}'.");

        if (!OperationTypes.Implemented.Contains(step.Type))
            throw new NotImplementedException($"Operation type '{step.Type}' is defined but not yet implemented.");

        if (OperationTypes.StatisticOperations.Contains(step.Type))
        {
            await ExecuteStatisticStepAsync(step);
            return;
        }

        if (OperationTypes.LeaderboardOperations.Contains(step.Type))
        {
            await ExecuteLeaderboardStepAsync(step);
            return;
        }

        if (step.Keys is not null)
        {
            foreach (var key in step.Keys)
                await ExecuteSingleAsync(step.Type, key, null);
        }
        else if (step.Values is not null)
        {
            foreach (var kvp in step.Values)
                await ExecuteSingleAsync(step.Type, kvp.Key, kvp.Value);
        }
        else
        {
            await ExecuteSingleAsync(step.Type, step.Key!, step.Value);
        }
    }

    private static async Task ExecuteSingleAsync(string type, string key, string? value)
    {
        switch (type)
        {
            case OperationTypes.SetTitleData:
                await SetTitleDataAsync(key, value);
                break;
            case OperationTypes.DeleteTitleData:
                await SetTitleDataAsync(key, null);
                break;
            case OperationTypes.SetTitleInternalData:
                await SetTitleInternalDataAsync(key, value);
                break;
            case OperationTypes.DeleteTitleInternalData:
                await SetTitleInternalDataAsync(key, null);
                break;
        }
    }

    private static string StepTarget(MigrationStep step)
    {
        if (step.Key is not null) return step.Key;
        if (step.Keys is not null) return $"[{string.Join(", ", step.Keys)}]";
        if (step.Values is not null) return $"[{string.Join(", ", step.Values.Keys)}]";
        if (step.Name is not null) return step.Name;
        return "?";
    }

    private static async Task ExecuteStatisticStepAsync(MigrationStep step)
    {
        switch (step.Type)
        {
            case OperationTypes.CreateStatistic:
                await CreateStatisticDefinitionAsync(step);
                break;
            case OperationTypes.DeleteStatistic:
                await DeleteStatisticDefinitionAsync(step.Name!);
                break;
        }
    }

    private static async Task CreateStatisticDefinitionAsync(MigrationStep step)
    {
        var columns = step.Columns is not null
            ? step.Columns.Select(c => new StatisticColumn
            {
                Name = c.Name,
                AggregationMethod = ParseAggregationMethod(c.AggregationMethod),
            }).ToList()
            : [new StatisticColumn { Name = "Value", AggregationMethod = ParseAggregationMethod(step.AggregationMethod) }];

        var result = await PlayFabRetry.ExecuteWithThrottleRetryAsync(() => PlayFabProgressionAPI.CreateStatisticDefinitionAsync(new CreateStatisticDefinitionRequest
        {
            Name = step.Name!,
            EntityType = step.EntityType ?? "title_player_account",
            Columns = columns,
            VersionConfiguration = BuildVersionConfiguration(step),
        }));

        // Idempotent: a retried `migrate up` after a partial failure will re-attempt already-created
        // definitions. Treat "already exists" as success so it can resume instead of failing on step 1.
        if (result.Error is not null && result.Error.Error != PlayFabErrorCode.DuplicateStatisticName)
            throw new InvalidOperationException($"CreateStatisticDefinition failed for '{step.Name}': {result.Error.ErrorMessage}");
    }

    private static async Task DeleteStatisticDefinitionAsync(string name)
    {
        var result = await PlayFabRetry.ExecuteWithThrottleRetryAsync(() => PlayFabProgressionAPI.DeleteStatisticDefinitionAsync(new DeleteStatisticDefinitionRequest { Name = name }));
        if (result.Error is not null)
            throw new InvalidOperationException($"DeleteStatisticDefinition failed for '{name}': {result.Error.ErrorMessage}");
    }

    private static async Task ExecuteLeaderboardStepAsync(MigrationStep step)
    {
        switch (step.Type)
        {
            case OperationTypes.CreateLeaderboard:
                await CreateLeaderboardDefinitionAsync(step);
                break;
            case OperationTypes.DeleteLeaderboard:
                await DeleteLeaderboardDefinitionAsync(step.Name!);
                break;
        }
    }

    private static async Task CreateLeaderboardDefinitionAsync(MigrationStep step)
    {
        var columns = step.LeaderboardColumns!.Select(c =>
        {
            var column = new LeaderboardColumn
            {
                Name = c.Name,
                SortDirection = ParseSortDirection(c.SortDirection),
            };

            if (c.LinkedStatistic is not null)
            {
                column.LinkedStatisticColumn = new LinkedStatisticColumn
                {
                    LinkedStatisticName = c.LinkedStatistic,
                    LinkedStatisticColumnName = c.LinkedStatisticColumn!,
                };
            }

            return column;
        }).ToList();

        var result = await PlayFabRetry.ExecuteWithThrottleRetryAsync(() => PlayFabProgressionAPI.CreateLeaderboardDefinitionAsync(new CreateLeaderboardDefinitionRequest
        {
            Name = step.Name!,
            EntityType = step.EntityType ?? "title_player_account",
            Columns = columns,
            SizeLimit = step.SizeLimit ?? 1000,
            VersionConfiguration = BuildVersionConfiguration(step),
        }));

        // Idempotent for the same reason as CreateStatisticDefinitionAsync above.
        if (result.Error is not null && result.Error.Error != PlayFabErrorCode.LeaderboardNameConflict)
            throw new InvalidOperationException($"CreateLeaderboardDefinition failed for '{step.Name}': {result.Error.ErrorMessage}");
    }

    private static async Task DeleteLeaderboardDefinitionAsync(string name)
    {
        var result = await PlayFabRetry.ExecuteWithThrottleRetryAsync(() => PlayFabProgressionAPI.DeleteLeaderboardDefinitionAsync(new DeleteLeaderboardDefinitionRequest { Name = name }));
        if (result.Error is not null)
            throw new InvalidOperationException($"DeleteLeaderboardDefinition failed for '{name}': {result.Error.ErrorMessage}");
    }

    private static PlayFab.ProgressionModels.StatisticAggregationMethod ParseAggregationMethod(string? value) =>
        Enum.Parse<PlayFab.ProgressionModels.StatisticAggregationMethod>(value ?? "Last", ignoreCase: true);

    private static PlayFab.ProgressionModels.ResetInterval ParseResetInterval(string? value) =>
        Enum.Parse<PlayFab.ProgressionModels.ResetInterval>(value ?? "Manual", ignoreCase: true);

    private static LeaderboardSortDirection ParseSortDirection(string value) =>
        Enum.Parse<LeaderboardSortDirection>(value, ignoreCase: true);

    private static VersionConfiguration BuildVersionConfiguration(MigrationStep step) => new()
    {
        ResetInterval = ParseResetInterval(step.ResetInterval),
        MaxQueryableVersions = step.MaxQueryableVersions ?? 1,
    };

    private static async Task SetTitleDataAsync(string key, string? value)
    {
        var result = await PlayFabRetry.ExecuteWithThrottleRetryAsync(() => PlayFabAdminAPI.SetTitleDataAsync(new SetTitleDataRequest { Key = key, Value = value }));
        if (result.Error is not null)
            throw new InvalidOperationException($"SetTitleData failed for key '{key}': {result.Error.ErrorMessage}");
    }

    private static async Task SetTitleInternalDataAsync(string key, string? value)
    {
        var result = await PlayFabRetry.ExecuteWithThrottleRetryAsync(() => PlayFabAdminAPI.SetTitleInternalDataAsync(new SetTitleDataRequest { Key = key, Value = value }));
        if (result.Error is not null)
            throw new InvalidOperationException($"SetTitleInternalData failed for key '{key}': {result.Error.ErrorMessage}");
    }
}
