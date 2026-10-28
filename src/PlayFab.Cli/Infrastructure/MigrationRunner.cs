using PlayFab;
using PlayFab.AdminModels;
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
        return "?";
    }

    private static async Task SetTitleDataAsync(string key, string? value)
    {
        var result = await PlayFabAdminAPI.SetTitleDataAsync(new SetTitleDataRequest { Key = key, Value = value });
        if (result.Error is not null)
            throw new InvalidOperationException($"SetTitleData failed for key '{key}': {result.Error.ErrorMessage}");
    }

    private static async Task SetTitleInternalDataAsync(string key, string? value)
    {
        var result = await PlayFabAdminAPI.SetTitleInternalDataAsync(new SetTitleDataRequest { Key = key, Value = value });
        if (result.Error is not null)
            throw new InvalidOperationException($"SetTitleInternalData failed for key '{key}': {result.Error.ErrorMessage}");
    }
}
