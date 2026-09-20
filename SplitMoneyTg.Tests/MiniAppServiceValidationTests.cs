using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SplitMoneyTg.Api;
using SplitMoneyTg.Application;
using SplitMoneyTg.Domain;
using SplitMoneyTg.Infrastructure;
using Telegram.Bot;
using Xunit;

namespace SplitMoneyTg.Tests;

public sealed class MiniAppServiceValidationTests
{
    [Fact]
    public void ValidateExpense_AcceptsExactShares()
    {
        MiniAppService.ValidateExpense(new ExpenseRequest("Dinner", 1001, 1,
            [new ExpenseShareRequest(1, 501), new ExpenseShareRequest(2, 500)]));
    }

    [Fact]
    public void ValidateExpense_RejectsMismatchedTotal()
    {
        var error = Assert.Throws<ApiException>(() => MiniAppService.ValidateExpense(new ExpenseRequest("Dinner", 1000, 1,
            [new ExpenseShareRequest(1, 500), new ExpenseShareRequest(2, 499)])));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("share_total_mismatch", error.Code);
    }

    [Fact]
    public void ValidateExpense_RejectsDuplicateParticipant()
    {
        var error = Assert.Throws<ApiException>(() => MiniAppService.ValidateExpense(new ExpenseRequest("Dinner", 1000, 1,
            [new ExpenseShareRequest(2, 500), new ExpenseShareRequest(2, 500)])));

        Assert.Equal("duplicate_share", error.Code);
    }

    [Fact]
    public void ValidateExpense_RejectsAmountAboveExistingBotLimit()
    {
        var error = Assert.Throws<ApiException>(() => MiniAppService.ValidateExpense(new ExpenseRequest("Dinner", 1_000_000_000_001, 1,
            [new ExpenseShareRequest(1, 1_000_000_000_001)])));

        Assert.Equal("invalid_amount", error.Code);
    }

    [Fact]
    public async Task AddParticipant_RevalidatesStandaloneNames()
    {
        await using var db = CreateDb();
        db.Users.Add(new AppUser { TelegramId = 10, DisplayName = "Owner" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = CreateService(db);
        var group = await service.CreateGroup(10, new CreateGroupRequest("Trip", GroupType.Standalone), TestContext.Current.CancellationToken);

        var participant = await service.AddParticipant(10, group.Id, new ParticipantRequest("Alice", null), TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<ApiException>(() => service.AddParticipant(10, group.Id,
            new ParticipantRequest("alice", null), TestContext.Current.CancellationToken));

        Assert.True(participant.Id < 0);
        Assert.Equal("duplicate_participant_name", error.Code);
    }

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static MiniAppService CreateService(AppDbContext db) => new(db, new BalanceService(db),
        new TelegramBotClient("123456:test-token"), NullLogger<MiniAppService>.Instance);
}
