using PlayFab.ProgressionModels;
using PlayFab.Cli.Infrastructure;

namespace PlayFab.Cli.Commands.Leaderboard;

/// <summary>Resolved seeding targets for a given --name/--type selection.</summary>
internal sealed class SeedResolution
{
    public List<StatisticDefinition> Statistics { get; init; } = [];
    public List<LeaderboardDefinition> StandaloneLeaderboards { get; init; } = [];
    public List<LinkedLeaderboardInfo> LinkedLeaderboardNotes { get; init; } = [];
}

/// <summary>A leaderboard that can't be seeded directly because it's linked to one or more statistics.</summary>
internal sealed record LinkedLeaderboardInfo(string LeaderboardName, List<string> LinkedStatisticNames);

/// <summary>Builds and applies synthetic-data seeding plans against remote statistics and leaderboards.</summary>
internal static class SeedEngine
{
    private const string TitlePlayerAccountEntityType = "title_player_account";
    private const int MaxConcurrency = 5;

    /// <summary>Validates seed command options, returning a list of problems (empty if valid).</summary>
    public static List<string> ValidateOptions(string? type, int count, long min, long max, int batchSize)
    {
        var errors = new List<string>();

        if (type is not null
            && !type.Equals("statistic", StringComparison.OrdinalIgnoreCase)
            && !type.Equals("leaderboard", StringComparison.OrdinalIgnoreCase))
            errors.Add("--type must be 'statistic' or 'leaderboard'.");

        if (count <= 0)
            errors.Add("--count must be greater than zero.");

        if (min > max)
            errors.Add("--min must be less than or equal to --max.");

        if (batchSize <= 0)
            errors.Add("--batch-size must be greater than zero.");

        return errors;
    }

    /// <summary>Resolves which statistics/leaderboards to seed for a given --name/--type filter (all remote targets if --name is omitted).</summary>
    public static (SeedResolution? Resolution, string? Error) ResolveTargets(
        List<StatisticDefinition> stats, List<LeaderboardDefinition> leaderboards, string? name, string? type)
    {
        if (name is null)
        {
            var standalone = new List<LeaderboardDefinition>();
            var linkedNotes = new List<LinkedLeaderboardInfo>();

            foreach (var leaderboard in leaderboards)
            {
                var linkedNames = GetLinkedStatisticNames(leaderboard);
                if (linkedNames.Count > 0)
                    linkedNotes.Add(new LinkedLeaderboardInfo(leaderboard.Name, linkedNames));
                else
                    standalone.Add(leaderboard);
            }

            return (new SeedResolution { Statistics = stats, StandaloneLeaderboards = standalone, LinkedLeaderboardNotes = linkedNotes }, null);
        }

        var stat = type is null || type.Equals("statistic", StringComparison.OrdinalIgnoreCase)
            ? stats.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            : null;
        var lb = type is null || type.Equals("leaderboard", StringComparison.OrdinalIgnoreCase)
            ? leaderboards.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            : null;

        if (stat is null && lb is null)
            return (null, $"'{name}' not found among remote statistics or leaderboards.");

        if (stat is not null && lb is not null)
            return (null, $"'{name}' matches both a statistic and a leaderboard. Use --type statistic or --type leaderboard to disambiguate.");

        if (lb is not null)
        {
            var linkedNames = GetLinkedStatisticNames(lb);

            if (linkedNames.Count == 0)
                return (new SeedResolution { StandaloneLeaderboards = [lb] }, null);

            var linkedStats = new List<StatisticDefinition>();
            var missing = new List<string>();

            foreach (var statName in linkedNames)
            {
                var linkedStat = stats.FirstOrDefault(s => s.Name.Equals(statName, StringComparison.OrdinalIgnoreCase));
                if (linkedStat is null)
                    missing.Add(statName);
                else
                    linkedStats.Add(linkedStat);
            }

            if (missing.Count > 0)
                return (null, $"Linked statistic '{string.Join(", ", missing)}' not found remotely; cannot seed '{name}'.");

            return (new SeedResolution
            {
                Statistics = linkedStats,
                LinkedLeaderboardNotes = [new LinkedLeaderboardInfo(lb.Name, linkedNames)],
            }, null);
        }

        return (new SeedResolution { Statistics = [stat!] }, null);
    }

    private static List<string> GetLinkedStatisticNames(LeaderboardDefinition lb) =>
        (lb.Columns ?? [])
            .Select(c => c.LinkedStatisticColumn?.LinkedStatisticName)
            .Where(n => n is not null)
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Generates one uniform-random value per column, in [min, max] inclusive.</summary>
    public static List<string> GenerateScores(int columnCount, Random rng, long min, long max)
    {
        var scores = new List<string>(columnCount);
        for (var i = 0; i < columnCount; i++)
            scores.Add(rng.NextInt64(min, max + 1).ToString());
        return scores;
    }

    /// <summary>How many more accounts are needed to reach the desired count.</summary>
    public static int CalculateShortfall(int existingCount, int desiredCount) => Math.Max(0, desiredCount - existingCount);

    /// <summary>Tops up a stored account list to the desired count, creating new synthetic accounts as needed.</summary>
    public static async Task<List<SyntheticAccount>> EnsureAccounts(List<SyntheticAccount> existing, int desiredCount, string namePrefix)
    {
        var shortfall = CalculateShortfall(existing.Count, desiredCount);
        if (shortfall == 0)
            return existing;

        using var semaphore = new SemaphoreSlim(MaxConcurrency);
        var tasks = new List<Task<SyntheticAccount?>>();

        for (var i = 0; i < shortfall; i++)
        {
            var index = existing.Count + i;
            tasks.Add(CreateAccountBounded(semaphore, $"playfab-cli-seed-{Guid.NewGuid():N}", $"{namePrefix}{index + 1:D4}"));
        }

        var created = await Task.WhenAll(tasks);

        var result = new List<SyntheticAccount>(existing);
        result.AddRange(created.Where(a => a is not null).Select(a => a!));
        return result;
    }

    private static async Task<SyntheticAccount?> CreateAccountBounded(SemaphoreSlim semaphore, string serverCustomId, string displayName)
    {
        await semaphore.WaitAsync();
        try
        {
            return await SyntheticAccountClient.CreateAccount(serverCustomId, displayName);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>Pushes synthetic values for a statistic, one call per account (also feeds any leaderboard linked to it).</summary>
    public static async Task<(int Success, int Failed)> SeedStatistic(StatisticDefinition stat, List<SyntheticAccount> accounts, long min, long max, Random rng)
    {
        var columnCount = Math.Max(1, stat.Columns?.Count ?? 1);
        var jobs = accounts.Select(a => (Account: a, Scores: GenerateScores(columnCount, rng, min, max))).ToList();

        using var semaphore = new SemaphoreSlim(MaxConcurrency);
        var tasks = jobs.Select(job => PushStatisticBounded(semaphore, stat.Name, job.Account, job.Scores)).ToList();
        var results = await Task.WhenAll(tasks);

        return (results.Count(r => r), results.Count(r => !r));
    }

    private static async Task<bool> PushStatisticBounded(SemaphoreSlim semaphore, string statName, SyntheticAccount account, List<string> scores)
    {
        await semaphore.WaitAsync();
        try
        {
            var entity = new EntityKey { Id = account.EntityId, Type = TitlePlayerAccountEntityType };
            return await ProgressionApiClient.UpdateStatisticValue(entity, statName, scores);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>Pushes synthetic entries directly into a standalone leaderboard, batched up to batchSize per call.</summary>
    public static async Task<(int Success, int Failed)> SeedLeaderboard(LeaderboardDefinition lb, List<SyntheticAccount> accounts, long min, long max, Random rng, int batchSize)
    {
        var columnCount = Math.Max(1, lb.Columns?.Count ?? 1);
        var entries = accounts
            .Select(a => new LeaderboardEntryUpdate { EntityId = a.EntityId, Scores = GenerateScores(columnCount, rng, min, max) })
            .ToList();

        var success = 0;
        var failed = 0;

        foreach (var chunk in entries.Chunk(batchSize))
        {
            var ok = await ProgressionApiClient.UpdateLeaderboardEntriesBatch(lb.Name, chunk.ToList());
            if (ok) success += chunk.Length;
            else failed += chunk.Length;
        }

        return (success, failed);
    }

    /// <summary>Prints the dry-run plan: resolved targets, account shortfall, and value range.</summary>
    public static void PrintDryRunPlan(SeedResolution resolution, int storedCount, int desiredCount, long min, long max, int batchSize)
    {
        var shortfall = CalculateShortfall(storedCount, desiredCount);

        Console.WriteLine("Targets:");
        if (resolution.Statistics.Count > 0)
            Console.WriteLine($"  Statistics ({resolution.Statistics.Count}): {string.Join(", ", resolution.Statistics.Select(s => s.Name))}");
        if (resolution.StandaloneLeaderboards.Count > 0)
            Console.WriteLine($"  Leaderboards seeded directly ({resolution.StandaloneLeaderboards.Count}): {string.Join(", ", resolution.StandaloneLeaderboards.Select(l => l.Name))}");
        if (resolution.LinkedLeaderboardNotes.Count > 0)
        {
            Console.WriteLine("  Leaderboards seeded indirectly via linked statistic:");
            foreach (var note in resolution.LinkedLeaderboardNotes)
                Console.WriteLine($"    {note.LeaderboardName} -> {string.Join(", ", note.LinkedStatisticNames)}");
        }

        Console.WriteLine();
        Console.WriteLine($"Accounts: {storedCount} stored, need {desiredCount} -> will create {shortfall} new.");
        Console.WriteLine($"Values: random integers in [{min}, {max}] per column.");
        Console.WriteLine($"Leaderboard writes: batched up to {batchSize} entries/call.");
        Console.WriteLine();
        Console.WriteLine("Run with --apply to create accounts and push data.");
    }
}
