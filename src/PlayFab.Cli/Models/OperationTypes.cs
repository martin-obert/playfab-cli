namespace PlayFab.Cli.Models;

public static class OperationTypes
{
    public const string SetTitleData = "set-title-data";
    public const string DeleteTitleData = "delete-title-data";
    public const string SetTitleInternalData = "set-title-internal-data";
    public const string DeleteTitleInternalData = "delete-title-internal-data";

    public const string CreateStatistic = "create-statistic";
    public const string DeleteStatistic = "delete-statistic";
    public const string ResetStatistic = "reset-statistic";
    public const string CreateLeaderboard = "create-leaderboard";
    public const string DeleteLeaderboard = "delete-leaderboard";
    public const string SetPolicy = "set-policy";

    public static readonly HashSet<string> All =
    [
        SetTitleData, DeleteTitleData,
        SetTitleInternalData, DeleteTitleInternalData,
        CreateStatistic, DeleteStatistic, ResetStatistic,
        CreateLeaderboard, DeleteLeaderboard, SetPolicy,
    ];

    public static readonly HashSet<string> Implemented =
    [
        SetTitleData, DeleteTitleData,
        SetTitleInternalData, DeleteTitleInternalData,
        CreateStatistic, DeleteStatistic,
        CreateLeaderboard, DeleteLeaderboard,
    ];

    /// <summary>Operations with a structured step shape (name/columns) instead of the flat key/value shape.</summary>
    public static readonly HashSet<string> StatisticOperations =
    [
        CreateStatistic, DeleteStatistic,
    ];

    /// <summary>Operations with a structured step shape (name/columns) instead of the flat key/value shape.</summary>
    public static readonly HashSet<string> LeaderboardOperations =
    [
        CreateLeaderboard, DeleteLeaderboard,
    ];
}
