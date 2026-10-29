namespace PlayFab.Cli.Models;

public sealed class SyntheticPlayerStore
{
    public required string TitleId { get; init; }
    public List<SyntheticPlayer> Players { get; init; } = [];
}

public sealed class SyntheticPlayer
{
    public required string PlayFabId { get; init; }
    public required string ServerCustomId { get; init; }
    public required string DisplayName { get; init; }
    public required DateTime CreatedAt { get; init; }
}
