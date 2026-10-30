using PlayFab.ProgressionModels;

namespace PlayFab.Cli.Infrastructure;

/// <summary>
/// PlayFab rejects deleting a statistic/leaderboard while a link between them exists, in both
/// directions (PlayFabErrorCode.StatisticDefinitionModificationNotAllowedWhileLinked /
/// LeaderboardDefinitionModificationNotAllowedWhileLinked) — there is no delete-time "force" flag, and
/// Update*DefinitionRequest has no Columns field, so the link can only be broken via the dedicated
/// UnlinkLeaderboardFromStatisticAsync endpoint. This wraps delete-statistic/delete-leaderboard so every
/// caller (migration steps and the nuke commands) gets "discover links, unlink, retry once" for free.
/// </summary>
public static class LinkedDefinitionDeleter
{
    public static async Task DeleteStatisticAsync(string name)
    {
        var result = await PlayFabRetry.ExecuteWithThrottleRetryAsync(() =>
            PlayFabProgressionAPI.DeleteStatisticDefinitionAsync(new DeleteStatisticDefinitionRequest { Name = name }));

        if (result.Error is null)
            return;

        if (result.Error.Error != PlayFabErrorCode.StatisticDefinitionModificationNotAllowedWhileLinked)
            throw new InvalidOperationException($"DeleteStatisticDefinition failed for '{name}': {result.Error.ErrorMessage}");

        await UnlinkStatisticAsync(name);

        var retryResult = await PlayFabRetry.ExecuteWithThrottleRetryAsync(() =>
            PlayFabProgressionAPI.DeleteStatisticDefinitionAsync(new DeleteStatisticDefinitionRequest { Name = name }));
        if (retryResult.Error is not null)
            throw new InvalidOperationException($"DeleteStatisticDefinition failed for '{name}' even after unlinking: {retryResult.Error.ErrorMessage}");
    }

    public static async Task DeleteLeaderboardAsync(string name)
    {
        var result = await PlayFabRetry.ExecuteWithThrottleRetryAsync(() =>
            PlayFabProgressionAPI.DeleteLeaderboardDefinitionAsync(new DeleteLeaderboardDefinitionRequest { Name = name }));

        if (result.Error is null)
            return;

        if (result.Error.Error != PlayFabErrorCode.LeaderboardDefinitionModificationNotAllowedWhileLinked)
            throw new InvalidOperationException($"DeleteLeaderboardDefinition failed for '{name}': {result.Error.ErrorMessage}");

        await UnlinkLeaderboardAsync(name);

        var retryResult = await PlayFabRetry.ExecuteWithThrottleRetryAsync(() =>
            PlayFabProgressionAPI.DeleteLeaderboardDefinitionAsync(new DeleteLeaderboardDefinitionRequest { Name = name }));
        if (retryResult.Error is not null)
            throw new InvalidOperationException($"DeleteLeaderboardDefinition failed for '{name}' even after unlinking: {retryResult.Error.ErrorMessage}");
    }

    private static async Task UnlinkStatisticAsync(string statisticName)
    {
        var definitionResult = await PlayFabRetry.ExecuteWithThrottleRetryAsync(() =>
            PlayFabProgressionAPI.GetStatisticDefinitionAsync(new GetStatisticDefinitionRequest { Name = statisticName }));
        if (definitionResult.Error is not null)
            throw new InvalidOperationException(
                $"GetStatisticDefinition failed for '{statisticName}' while resolving links: {definitionResult.Error.ErrorMessage}");

        foreach (var leaderboardName in definitionResult.Result.LinkedLeaderboardNames ?? [])
            await UnlinkAsync(leaderboardName, statisticName);
    }

    private static async Task UnlinkLeaderboardAsync(string leaderboardName)
    {
        var definitionResult = await PlayFabRetry.ExecuteWithThrottleRetryAsync(() =>
            PlayFabProgressionAPI.GetLeaderboardDefinitionAsync(new GetLeaderboardDefinitionRequest { Name = leaderboardName }));
        if (definitionResult.Error is not null)
            throw new InvalidOperationException(
                $"GetLeaderboardDefinition failed for '{leaderboardName}' while resolving links: {definitionResult.Error.ErrorMessage}");

        var statisticNames = (definitionResult.Result.Columns ?? [])
            .Select(c => c.LinkedStatisticColumn?.LinkedStatisticName)
            .Where(n => n is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var statisticName in statisticNames)
            await UnlinkAsync(leaderboardName, statisticName!);
    }

    private static async Task UnlinkAsync(string leaderboardName, string statisticName)
    {
        var result = await PlayFabRetry.ExecuteWithThrottleRetryAsync(() =>
            PlayFabProgressionAPI.UnlinkLeaderboardFromStatisticAsync(new UnlinkLeaderboardFromStatisticRequest
            {
                Name = leaderboardName,
                StatisticName = statisticName,
            }));

        // Idempotent: a retried nuke/migration may re-attempt unlinking a pair an earlier partial run
        // already unlinked.
        if (result.Error is not null && result.Error.Error != PlayFabErrorCode.NoLinkedStatisticToLeaderboard)
            throw new InvalidOperationException(
                $"UnlinkLeaderboardFromStatistic failed for leaderboard '{leaderboardName}' / statistic '{statisticName}': {result.Error.ErrorMessage}");
    }
}
