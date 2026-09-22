using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SplitMoneyTg.Api;
using SplitMoneyTg.Application;
using SplitMoneyTg.Domain;
using SplitMoneyTg.Infrastructure;
using Telegram.Bot;
using Xunit;

namespace SplitMoneyTg.Tests;

public sealed class BalanceDetailsTests
{
    [Fact]
    public async Task GetBalanceDetails_ReturnsExpensesRelevantTransfersAndCurrentTotals()
    {
        await using var db = CreateDb();
        var groupId = Guid.NewGuid();
        var group = new ExpenseGroup { Id = groupId, Name = "Trip", OwnerId = 10, Type = GroupType.Collective };
        var alice = new AppUser { TelegramId = 10, DisplayName = "Alice" };
        var bob = new AppUser { TelegramId = 20, DisplayName = "Bob" };
        db.AddRange(alice, bob, group,
            new GroupMember { GroupId = groupId, UserId = 10, Group = group, User = alice },
            new GroupMember { GroupId = groupId, UserId = 20, Group = group, User = bob },
            new GroupParticipant { GroupId = groupId, ParticipantId = 10, TelegramUserId = 10, Group = group, TelegramUser = alice },
            new GroupParticipant { GroupId = groupId, ParticipantId = 20, TelegramUserId = 20, Group = group, TelegramUser = bob });
        var expense = new Expense
        {
            GroupId = groupId, AuthorId = 10, PayerId = 10, Description = "Dinner", AmountKopecks = 3000,
            CreatedAt = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero)
        };
        expense.Shares.AddRange([
            new ExpenseShare { GroupId = groupId, ExpenseId = expense.Id, UserId = 20, AmountKopecks = 2000 },
            new ExpenseShare { GroupId = groupId, ExpenseId = expense.Id, UserId = 10, AmountKopecks = 1000 }
        ]);
        db.Add(expense);
        db.Transfers.AddRange(
            Transfer(groupId, 20, 10, 500, TransferStatus.Confirmed, 11),
            Transfer(groupId, 20, 10, 1500, TransferStatus.Pending, 12),
            Transfer(groupId, 20, 10, 200, TransferStatus.Rejected, 13),
            Transfer(groupId, 20, 10, 300, TransferStatus.Cancelled, 14));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var before = DateTimeOffset.UtcNow;

        var result = await CreateService(db).GetBalanceDetails(10, groupId, TestContext.Current.CancellationToken);

        Assert.Equal("Trip", result.GroupName);
        Assert.True(result.GeneratedAt >= before);
        var returnedExpense = Assert.Single(result.Expenses);
        Assert.Equal("Dinner", returnedExpense.Description);
        Assert.Equal(["Alice", "Bob"], returnedExpense.Shares.Select(x => x.ParticipantName));
        Assert.Collection(result.Transfers,
            pending => Assert.Equal(TransferStatus.Pending, pending.Status),
            confirmed => Assert.Equal(TransferStatus.Confirmed, confirmed.Status));
        Assert.Collection(result.Balances,
            balance => { Assert.Equal("Alice", balance.ParticipantName); Assert.Equal(1500, balance.AmountKopecks); },
            balance => { Assert.Equal("Bob", balance.ParticipantName); Assert.Equal(-1500, balance.AmountKopecks); });
        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal(20, suggestion.FromParticipantId);
        Assert.Equal(10, suggestion.ToParticipantId);
        Assert.Equal(1500, suggestion.AmountKopecks);
        Assert.True(suggestion.IsPending);
    }

    [Fact]
    public async Task GetBalanceDetails_PreservesManagedParticipantIdentity()
    {
        await using var db = CreateDb();
        var groupId = Guid.NewGuid();
        var group = new ExpenseGroup { Id = groupId, Name = "Home", OwnerId = 10, Type = GroupType.Standalone };
        var owner = new AppUser { TelegramId = 10, DisplayName = "Owner" };
        db.AddRange(owner, group,
            new GroupMember { GroupId = groupId, UserId = 10, Group = group, User = owner },
            new GroupParticipant { GroupId = groupId, ParticipantId = 10, TelegramUserId = 10, Group = group, TelegramUser = owner },
            new GroupParticipant { GroupId = groupId, ParticipantId = -1, DisplayName = "Cash", Group = group });
        var expense = new Expense { GroupId = groupId, AuthorId = 10, PayerId = -1, Description = "Groceries", AmountKopecks = 1000 };
        expense.Shares.AddRange([
            new ExpenseShare { GroupId = groupId, ExpenseId = expense.Id, UserId = -1, AmountKopecks = 500 },
            new ExpenseShare { GroupId = groupId, ExpenseId = expense.Id, UserId = 10, AmountKopecks = 500 }
        ]);
        db.Add(expense);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).GetBalanceDetails(10, groupId, TestContext.Current.CancellationToken);

        var returnedExpense = Assert.Single(result.Expenses);
        Assert.Equal(-1, returnedExpense.PayerId);
        Assert.Equal("Cash", returnedExpense.PayerName);
        Assert.Contains(returnedExpense.Shares, x => x.ParticipantId == -1 && x.ParticipantName == "Cash");
    }

    [Fact]
    public async Task GetBalanceDetails_HidesGroupFromNonMember()
    {
        await using var db = CreateDb();
        var groupId = Guid.NewGuid();
        db.AddRange(new AppUser { TelegramId = 10, DisplayName = "Owner" },
            new ExpenseGroup { Id = groupId, Name = "Private", OwnerId = 10, Type = GroupType.Collective });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            CreateService(db).GetBalanceDetails(99, groupId, TestContext.Current.CancellationToken));

        Assert.Equal(404, error.StatusCode);
    }

    private static Transfer Transfer(Guid groupId, long from, long to, long amount, TransferStatus status, int hour) => new()
    {
        GroupId = groupId, FromUserId = from, ToUserId = to, AmountKopecks = amount, Status = status,
        CreatedAt = new DateTimeOffset(2026, 9, 20, hour, 0, 0, TimeSpan.Zero),
        ResolvedAt = status == TransferStatus.Pending ? null : new DateTimeOffset(2026, 9, 20, hour, 30, 0, TimeSpan.Zero)
    };

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static MiniAppService CreateService(AppDbContext db) => new(db, new BalanceService(db),
        new TelegramBotClient("123456:test-token"), new PostCommitActions(), NullLogger<MiniAppService>.Instance);
}
