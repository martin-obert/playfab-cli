using System.Text.Json;
using PlayFab;
using PlayFab.AdminModels;
using PlayFab.Cli.Models;

namespace PlayFab.Cli.Infrastructure;

public static class MigrationLedgerService
{
    public const string LedgerKey = "__playfab_cli_migrations";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static async Task<MigrationLedger> ReadLedgerAsync()
    {
        var request = new GetTitleDataRequest { Keys = [LedgerKey] };
        var result = await PlayFabAdminAPI.GetTitleInternalDataAsync(request);

        if (result.Error is not null)
            throw new InvalidOperationException($"Failed to read migration ledger: {result.Error.ErrorMessage}");

        if (result.Result.Data.TryGetValue(LedgerKey, out var json) && !string.IsNullOrEmpty(json))
        {
            return JsonSerializer.Deserialize<MigrationLedger>(json, JsonOptions) ?? new MigrationLedger();
        }

        return new MigrationLedger();
    }

    public static async Task WriteLedgerAsync(MigrationLedger ledger)
    {
        var json = JsonSerializer.Serialize(ledger, JsonOptions);
        var request = new SetTitleDataRequest { Key = LedgerKey, Value = json };
        var result = await PlayFabAdminAPI.SetTitleInternalDataAsync(request);

        if (result.Error is not null)
            throw new InvalidOperationException($"Failed to write migration ledger: {result.Error.ErrorMessage}");
    }
}
