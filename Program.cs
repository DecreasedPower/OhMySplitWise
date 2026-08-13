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
    var telegram = scope.ServiceProvider.GetRequiredService<IOptions<TelegramOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(telegram.WebhookUrl))
    {
        var bot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        await bot.SetWebhook(telegram.WebhookUrl.TrimEnd('/') + "/telegram/webhook", secretToken: telegram.WebhookSecret);
    }
}

await app.RunAsync();
