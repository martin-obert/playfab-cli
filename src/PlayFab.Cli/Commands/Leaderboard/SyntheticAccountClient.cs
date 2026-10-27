using PlayFab;
using PlayFab.AdminModels;
using PlayFab.ServerModels;
using PlayFab.Cli.Infrastructure;

namespace PlayFab.Cli.Commands.Leaderboard;

/// <summary>Creates synthetic PlayFab accounts for leaderboard seeding, server-side via secret key only.</summary>
internal static class SyntheticAccountClient
{
    /// <summary>Creates a new title_player_account and sets its display name. Returns null on hard failure.</summary>
    public static async Task<SyntheticAccount?> CreateAccount(string serverCustomId, string displayName)
    {
        var login = await PlayFabServerAPI.LoginWithServerCustomIdAsync(new LoginWithServerCustomIdRequest
        {
            ServerCustomId = serverCustomId,
            CreateAccount = true,
        });

        if (login.Error is not null)
        {
            Console.Error.WriteLine($"Failed to create synthetic account '{serverCustomId}': {login.Error.ErrorMessage}");
            Environment.ExitCode = 1;
            return null;
        }

        var entityId = login.Result.EntityToken?.Entity?.Id;
        if (entityId is null)
        {
            Console.Error.WriteLine($"Account '{serverCustomId}' was created but returned no entity id; cannot use it for seeding.");
            Environment.ExitCode = 1;
            return null;
        }

        var nameResult = await PlayFabAdminAPI.UpdateUserTitleDisplayNameAsync(new UpdateUserTitleDisplayNameRequest
        {
            PlayFabId = login.Result.PlayFabId,
            DisplayName = displayName,
        });

        if (nameResult.Error is not null)
            Console.Error.WriteLine($"Warning: created account '{login.Result.PlayFabId}' but failed to set its display name: {nameResult.Error.ErrorMessage}");

        return new SyntheticAccount
        {
            PlayFabId = login.Result.PlayFabId,
            EntityId = entityId,
            ServerCustomId = serverCustomId,
            DisplayName = displayName,
            CreatedUtc = DateTime.UtcNow,
        };
    }
}
