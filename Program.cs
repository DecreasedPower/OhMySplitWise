using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SplitMoneyTg.Application;
using SplitMoneyTg.Infrastructure;
using SplitMoneyTg.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<TelegramOptions>().Bind(builder.Configuration.GetSection(TelegramOptions.Section)).Validate(x =>
    !string.IsNullOrWhiteSpace(x.BotToken) &&
    !string.IsNullOrWhiteSpace(x.WebhookSecret), "Telegram BotToken and WebhookSecret are required").ValidateOnStart();
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Postgres") ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required")));
builder.Services.AddScoped<BalanceService>();
builder.Services.AddScoped<BotHandler>();
builder.Services.AddSingleton<ITelegramBotClient>(sp => new TelegramBotClient(sp.GetRequiredService<IOptions<TelegramOptions>>().Value.BotToken));
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapPost("/telegram/webhook", async (HttpRequest request, Update update, BotHandler handler, IOptions<TelegramOptions> options, CancellationToken ct) =>
{
    if (!request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var secret) || secret != options.Value.WebhookSecret)
        return Results.Unauthorized();
    await handler.Handle(update, ct);
    return Results.Ok();
});
app.MapHealthChecks("/health");

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync(
        """
        INSERT INTO "GroupParticipants"
            ("GroupId", "ParticipantId", "TelegramUserId", "DisplayName", "PaymentDetails", "IsActive", "CreatedAt")
        SELECT gm."GroupId", gm."UserId", gm."UserId", NULL, NULL, gm."IsActive", gm."JoinedAt"
        FROM "GroupMembers" gm
        JOIN "Groups" g ON g."Id" = gm."GroupId"
        WHERE g."Type" = 0
        ON CONFLICT ("GroupId", "ParticipantId") DO UPDATE
        SET "TelegramUserId" = EXCLUDED."TelegramUserId", "IsActive" = EXCLUDED."IsActive";
        """);
    var telegram = scope.ServiceProvider.GetRequiredService<IOptions<TelegramOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(telegram.WebhookUrl))
    {
        var bot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        await bot.SetWebhook(telegram.WebhookUrl.TrimEnd('/') + "/telegram/webhook", secretToken: telegram.WebhookSecret);
    }
}

await app.RunAsync();
