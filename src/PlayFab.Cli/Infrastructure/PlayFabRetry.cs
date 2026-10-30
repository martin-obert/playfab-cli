using PlayFab;

namespace PlayFab.Cli.Infrastructure;

public static class PlayFabRetry
{
    private const int MaxAttempts = 5;

    /// <summary>
    /// Retries a PlayFab call when throttled (PlayFabErrorCode.APIClientRequestRateLimitExceeded),
    /// waiting PlayFabError.RetryAfterSeconds (the server-specified cooldown) instead of guessing a delay.
    /// </summary>
    public static async Task<PlayFabResult<TResult>> ExecuteWithThrottleRetryAsync<TResult>(Func<Task<PlayFabResult<TResult>>> apiCall)
        where TResult : PlayFab.Internal.PlayFabResultCommon
    {
        var attempt = 0;

        while (true)
        {
            attempt++;
            var result = await apiCall();

            if (result.Error is null || result.Error.Error != PlayFabErrorCode.APIClientRequestRateLimitExceeded || attempt >= MaxAttempts)
                return result;

            var delaySeconds = result.Error.RetryAfterSeconds ?? 1;
            var retryAt = DateTime.Now.AddSeconds(delaySeconds);
            Console.WriteLine($"WARNING: Rate limited by PlayFab (attempt {attempt}/{MaxAttempts}); waiting until {retryAt:T} before retrying.");
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        }
    }
}
