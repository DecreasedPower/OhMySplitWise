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
        var shares = BalanceService.SplitEqually(100, [30, 10, 20], 20);

        Assert.Equal(33, shares[10]);
        Assert.Equal(34, shares[20]);
        Assert.Equal(33, shares[30]);
        Assert.Equal(100, shares.Values.Sum());
    }

    [Fact]
    public void SplitEqually_RotatesMultipleRemainderKopecksFromPayer()
    {
        var shares = BalanceService.SplitEqually(101, [30, 10, 20], 20);

        Assert.Equal(33, shares[10]);
        Assert.Equal(34, shares[20]);
        Assert.Equal(34, shares[30]);
    }

    [Fact]
    public void SplitEqually_SymmetricExpensesSettleAllParticipants()
    {
        long[] participantIds = [1, 2, 3];
        var balances = participantIds.ToDictionary(x => x, _ => 0L);

        foreach (var payerId in participantIds)
        {
            balances[payerId] += 100_000;
            foreach (var (participantId, share) in BalanceService.SplitEqually(100_000, participantIds, payerId))
                balances[participantId] -= share;
        }

        Assert.All(balances.Values, balance => Assert.Equal(0, balance));
    }

    [Fact]
    public void SplitEqually_UsesStableFallbackWhenPayerIsNotIncluded()
    {
        var shares = BalanceService.SplitEqually(100, [30, 10, 20], 40);

        Assert.Equal(34, shares[10]);
        Assert.Equal(33, shares[20]);
        Assert.Equal(33, shares[30]);
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
        var shares = BalanceService.SplitEqually(150_000, userIds, userIds[0]);

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
