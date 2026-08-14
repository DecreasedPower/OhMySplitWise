using SplitMoneyTg.Application;
using SplitMoneyTg.Domain;
using SplitMoneyTg.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SplitMoneyTg.Tests;

public sealed class BalanceServiceTests
{
    [Fact]
    public void SplitEqually_PreservesEveryKopeck()
    {
        var shares = BalanceService.SplitEqually(100, 3);

        Assert.Equal([34L, 33L, 33L], shares);
        Assert.Equal(100, shares.Sum());
    }

    [Fact]
    public void Minimize_SettlesBalancesWithAtMostNMinusOneTransfers()
    {
        var balances = new Dictionary<long, long>
        {
            [1] = -700,
            [2] = -300,
            [3] = 400,
            [4] = 600
        };

        var transfers = BalanceService.Minimize(balances);
        var settled = balances.ToDictionary(x => x.Key, x => x.Value);
        foreach (var transfer in transfers)
        {
            settled[transfer.FromUserId] += transfer.AmountKopecks;
            settled[transfer.ToUserId] -= transfer.AmountKopecks;
        }

        Assert.All(settled.Values, value => Assert.Equal(0, value));
        Assert.True(transfers.Count <= balances.Count - 1);
    }

    [Fact]
    public void Minimize_ReturnsNothingForSettledGroup()
    {
        Assert.Empty(BalanceService.Minimize(new Dictionary<long, long> { [1] = 0, [2] = 0 }));
    }

    [Fact]
    public void SplitEqually_CanBeMappedToTelegramIdsWithoutUsingIndexes()
    {
        var userIds = new[] { 368_900_896L, 1_697_173_796L };
        var amounts = BalanceService.SplitEqually(150_000, userIds.Length);
        var shares = new Dictionary<long, long>();
        for (var i = 0; i < userIds.Length; i++) shares[userIds[i]] = amounts[i];

        Assert.Equal(75_000, shares[368_900_896]);
        Assert.Equal(75_000, shares[1_697_173_796]);
        Assert.DoesNotContain(0, shares.Keys);
    }

    [Fact]
    public void SetShare_UsesParticipantTelegramIdInsteadOfIndex()
    {
        var shares = new Dictionary<long, long>();
        var participantIds = new[] { 368_900_896L, 1_697_173_796L };

        BalanceService.SetShare(shares, participantIds, 0, 40_000);
        BalanceService.SetShare(shares, participantIds, 1, 60_000);

        Assert.Equal(40_000, shares[368_900_896]);
        Assert.Equal(60_000, shares[1_697_173_796]);
        Assert.DoesNotContain(0, shares.Keys);
        Assert.DoesNotContain(1, shares.Keys);
    }

    [Fact]
    public void Minimize_SupportsManagedParticipantIds()
    {
        var transfers = BalanceService.Minimize(new Dictionary<long, long>
        {
            [-10] = -12_500,
            [-20] = 12_500
        });

        var transfer = Assert.Single(transfers);
        Assert.Equal(-10, transfer.FromUserId);
        Assert.Equal(-20, transfer.ToUserId);
        Assert.Equal(12_500, transfer.AmountKopecks);
    }

    [Fact]
    public void GroupType_DefaultsToCollectiveForExistingBehavior()
    {
        Assert.Equal(GroupType.Collective, new ExpenseGroup().Type);
    }

    [Fact]
    public async Task GetBalances_UsesManagedParticipantsInStandaloneGroup()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        var groupId = Guid.NewGuid();
        db.Groups.Add(new ExpenseGroup
        {
            Id = groupId,
            OwnerId = 100,
            Type = GroupType.Standalone,
            Participants =
            [
                new GroupParticipant { ParticipantId = 100 },
                new GroupParticipant { ParticipantId = -1, DisplayName = "Анна" },
                new GroupParticipant { ParticipantId = -2, DisplayName = "Борис" }
            ]
        });
        db.Expenses.Add(new Expense
        {
            GroupId = groupId,
            AuthorId = 100,
            PayerId = 100,
            AmountKopecks = 1_200,
            Shares =
            [
                new ExpenseShare { UserId = 100, AmountKopecks = 400 },
                new ExpenseShare { UserId = -1, AmountKopecks = 400 },
                new ExpenseShare { UserId = -2, AmountKopecks = 400 }
            ]
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var balances = await new BalanceService(db).GetBalances(groupId, TestContext.Current.CancellationToken);

        Assert.Equal(800, balances[100]);
        Assert.Equal(-400, balances[-1]);
        Assert.Equal(-400, balances[-2]);
    }
}
