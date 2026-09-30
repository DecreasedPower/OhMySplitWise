using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SplitMoneyTg.Application;
using SplitMoneyTg.Domain;
using SplitMoneyTg.Infrastructure;
using SplitMoneyTg.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace SplitMoneyTg.Tests;

public sealed class UsageMetricsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Metrics_CountArchivedGroupsAndDistinctUsersAtWindowBoundaries()
    {
        await using var db = CreateDb();
        DateTimeOffset?[] activity = [Now, Now.AddDays(-7), Now.AddDays(-7).AddTicks(-1),
            Now.AddDays(-30), Now.AddDays(-30).AddTicks(-1), null];
        for (var i = 0; i < activity.Length; i++)
            db.Users.Add(new AppUser { TelegramId = i + 1, LastActiveAt = activity[i] });
        db.Groups.AddRange(new ExpenseGroup { Name = "Current", OwnerId = 1 },
            new ExpenseGroup { Name = "Archived", OwnerId = 1, IsArchived = true });
        db.GroupParticipants.Add(new GroupParticipant { GroupId = db.Groups.Local.First().Id,
            ParticipantId = -1, DisplayName = "Managed" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new UsageMetricsService(db, new Clock(Now));

        await service.RecordActivity(1, TestContext.Current.CancellationToken);
        await service.RecordActivity(1, TestContext.Current.CancellationToken);
        var metrics = await service.GetMetrics(TestContext.Current.CancellationToken);

        Assert.Equal(2, metrics.TotalGroupsCreated);
        Assert.Equal(2, metrics.ActiveUsersLast7Days);
        Assert.Equal(4, metrics.ActiveUsersLast30Days);
    }

    [Fact]
    public async Task Activity_DoesNotMoveBackwardsOrChangeProfileVersion()
    {
        await using var db = CreateDb();
        db.Users.Add(new AppUser { TelegramId = 1, LastActiveAt = Now, Version = 5 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await new UsageMetricsService(db, new Clock(Now.AddDays(-1))).RecordActivity(1, TestContext.Current.CancellationToken);
        var user = await db.Users.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Now, user.LastActiveAt);
        Assert.Equal(5, user.Version);
    }

    [Theory]
    [InlineData("42", 42, ChatType.Private, true)]
    [InlineData("41, 42", 42, ChatType.Private, true)]
    [InlineData("42", 43, ChatType.Private, false)]
    [InlineData("", 42, ChatType.Private, false)]
    [InlineData("42", 42, ChatType.Group, false)]
    public async Task Stats_OnlyReportsInAdminPrivateChat(string adminIds, long userId, ChatType chatType, bool allowed)
    {
        await using var db = CreateDb();
        using var transport = new TelegramTransport();
        using var http = new HttpClient(transport);
        var metrics = new UsageMetricsService(db, new Clock(Now));
        var handler = new BotHandler(new TelegramBotClient("123456:test-token", http), db, new BalanceService(db),
            Options.Create(new TelegramOptions { StatsAdminIds = adminIds }), metrics);
        await handler.Handle(new Update { Id = 1, Message = new Message
        {
            From = new User { Id = userId, FirstName = "Test" },
            Chat = new Chat { Id = userId, Type = chatType }, Text = "/stats", Date = Now.UtcDateTime
        } }, TestContext.Current.CancellationToken);

        using var sentMessage = JsonDocument.Parse(transport.Requests.Single());
        Assert.Equal(allowed, sentMessage.RootElement.GetProperty("text").GetString()!.Contains("Всего групп создано"));
        Assert.Equal(Now, (await db.Users.SingleAsync(TestContext.Current.CancellationToken)).LastActiveAt);
    }

    [Fact]
    public async Task Bot_TracksCallbacksAndIgnoresRedeliveredUpdates()
    {
        await using var db = CreateDb();
        using var transport = new TelegramTransport();
        using var http = new HttpClient(transport);
        var clock = new Clock(Now);
        var handler = new BotHandler(new TelegramBotClient("123456:test-token", http), db, new BalanceService(db),
            Options.Create(new TelegramOptions()), new UsageMetricsService(db, clock));
        var update = new Update { Id = 7, CallbackQuery = new CallbackQuery
        {
            Id = "callback", From = new User { Id = 42, FirstName = "Test" }, Data = "unknown", ChatInstance = "test"
        } };
        await handler.Handle(update, TestContext.Current.CancellationToken);
        clock.Now = Now.AddDays(1);
        await handler.Handle(update, TestContext.Current.CancellationToken);
        Assert.Equal(Now, (await db.Users.SingleAsync(TestContext.Current.CancellationToken)).LastActiveAt);
        Assert.Single(transport.Requests);
        Assert.Single(await db.ProcessedUpdates.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TelegramTransport : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(ct));
            var result = request.RequestUri!.AbsolutePath.EndsWith("answerCallbackQuery")
                ? "true" : """{"message_id":1,"date":1790856000,"chat":{"id":42,"type":"private"},"text":"test"}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"ok\":true,\"result\":{result}}}", Encoding.UTF8, "application/json")
            };
        }
    }
}
