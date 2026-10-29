using PlayFab.Cli.Models;
using Xunit;

namespace PlayFab.Cli.Tests.Models;

public class OperationTypesTests
{
    [Fact]
    public void All_ContainsAllDefinedTypes()
    {
        Assert.Contains(OperationTypes.SetTitleData, OperationTypes.All);
        Assert.Contains(OperationTypes.DeleteTitleData, OperationTypes.All);
        Assert.Contains(OperationTypes.SetTitleInternalData, OperationTypes.All);
        Assert.Contains(OperationTypes.DeleteTitleInternalData, OperationTypes.All);
        Assert.Contains(OperationTypes.CreateStatistic, OperationTypes.All);
        Assert.Contains(OperationTypes.DeleteStatistic, OperationTypes.All);
        Assert.Contains(OperationTypes.ResetStatistic, OperationTypes.All);
        Assert.Contains(OperationTypes.CreateLeaderboard, OperationTypes.All);
        Assert.Contains(OperationTypes.DeleteLeaderboard, OperationTypes.All);
        Assert.Contains(OperationTypes.SetPolicy, OperationTypes.All);
        Assert.Equal(10, OperationTypes.All.Count);
    }

    [Fact]
    public void Implemented_IsSubsetOfAll()
    {
        Assert.Subset(OperationTypes.All, OperationTypes.Implemented);
    }

    [Fact]
    public void Implemented_ContainsTitleDataOperations()
    {
        Assert.Contains(OperationTypes.SetTitleData, OperationTypes.Implemented);
        Assert.Contains(OperationTypes.DeleteTitleData, OperationTypes.Implemented);
        Assert.Contains(OperationTypes.SetTitleInternalData, OperationTypes.Implemented);
        Assert.Contains(OperationTypes.DeleteTitleInternalData, OperationTypes.Implemented);
    }

    [Fact]
    public void Implemented_ContainsStatisticOperations()
    {
        Assert.Contains(OperationTypes.CreateStatistic, OperationTypes.Implemented);
        Assert.Contains(OperationTypes.DeleteStatistic, OperationTypes.Implemented);
    }

    [Fact]
    public void Implemented_ContainsLeaderboardOperations()
    {
        Assert.Contains(OperationTypes.CreateLeaderboard, OperationTypes.Implemented);
        Assert.Contains(OperationTypes.DeleteLeaderboard, OperationTypes.Implemented);
        Assert.Equal(8, OperationTypes.Implemented.Count);
    }

    [Fact]
    public void Implemented_DoesNotContainFutureTypes()
    {
        Assert.DoesNotContain(OperationTypes.ResetStatistic, OperationTypes.Implemented);
        Assert.DoesNotContain(OperationTypes.SetPolicy, OperationTypes.Implemented);
    }

    [Fact]
    public void StatisticOperations_ContainsExactlyCreateAndDelete()
    {
        Assert.Contains(OperationTypes.CreateStatistic, OperationTypes.StatisticOperations);
        Assert.Contains(OperationTypes.DeleteStatistic, OperationTypes.StatisticOperations);
        Assert.Equal(2, OperationTypes.StatisticOperations.Count);
    }

    [Fact]
    public void LeaderboardOperations_ContainsExactlyCreateAndDelete()
    {
        Assert.Contains(OperationTypes.CreateLeaderboard, OperationTypes.LeaderboardOperations);
        Assert.Contains(OperationTypes.DeleteLeaderboard, OperationTypes.LeaderboardOperations);
        Assert.Equal(2, OperationTypes.LeaderboardOperations.Count);
    }
}
