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
                return new MigrationRunResult(false, $"Step {stepsExecuted} ({step.Type} {step.Key}) failed: {ex.Message}", stepsExecuted);
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
                return new MigrationRunResult(false, $"Step {stepsExecuted} ({step.Type} {step.Key}) failed: {ex.Message}", stepsExecuted);
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

        switch (step.Type)
        {
            case OperationTypes.SetTitleData:
                await SetTitleDataAsync(step.Key, step.Value);
                break;
            case OperationTypes.DeleteTitleData:
                await SetTitleDataAsync(step.Key, null);
                break;
            case OperationTypes.SetTitleInternalData:
                await SetTitleInternalDataAsync(step.Key, step.Value);
                break;
            case OperationTypes.DeleteTitleInternalData:
                await SetTitleInternalDataAsync(step.Key, null);
                break;
        }
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
