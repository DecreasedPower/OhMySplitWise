using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SplitMoneyTg.Api;
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
builder.Services.AddScoped<MiniAppService>();
builder.Services.AddScoped<BotHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TelegramInitDataValidator>();
builder.Services.AddSingleton<ITelegramBotClient>(sp => new TelegramBotClient(sp.GetRequiredService<IOptions<TelegramOptions>>().Value.BotToken));
builder.Services.AddHealthChecks();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new LongAsStringJsonConverter());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("tma ", StringComparison.OrdinalIgnoreCase))
                throw new ApiException(401, "Unauthorized", "Authorization must use the tma scheme.", "missing_init_data");
            var validator = context.RequestServices.GetRequiredService<TelegramInitDataValidator>();
            var options = context.RequestServices.GetRequiredService<IOptions<TelegramOptions>>().Value;
            var user = validator.Validate(authorization[4..], options.BotToken);
            await context.RequestServices.GetRequiredService<MiniAppService>().UpsertUser(user, context.RequestAborted);
            context.Items[typeof(TelegramMiniAppUser)] = user;
        }
        await next(context);
    }
    catch (Exception exception) when (exception is ApiException or BadHttpRequestException or JsonException)
    {
        if (context.Response.HasStarted) throw;
        var apiException = exception as ApiException;
        context.Response.StatusCode = apiException?.StatusCode ?? 400;
        await Results.Problem(
            statusCode: context.Response.StatusCode,
            title: apiException?.Title ?? "Invalid request",
            detail: apiException?.Message ?? "The request body is invalid.",
            extensions: apiException?.Code is { } code ? new Dictionary<string, object?> { ["code"] = code } : null)
            .ExecuteAsync(context);
    }
    catch (Exception exception) when (context.Request.Path.StartsWithSegments("/api") && exception is not OperationCanceledException)
    {
        if (context.Response.HasStarted) throw;
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Api").LogError(exception, "Unhandled API error");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await Results.Problem(statusCode: 500, title: "Internal server error", detail: "An unexpected error occurred.",
            extensions: new Dictionary<string, object?> { ["code"] = "internal_error" }).ExecuteAsync(context);
    }
});
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/telegram/webhook", async (HttpRequest request, Update update, BotHandler handler, IOptions<TelegramOptions> options, CancellationToken ct) =>
{
    if (!request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var secret) || secret != options.Value.WebhookSecret)
        return Results.Unauthorized();
    await handler.Handle(update, ct);
    return Results.Ok();
});
app.MapHealthChecks("/health");

var api = app.MapGroup("/api");
api.MapGet("/me", (HttpContext context, MiniAppService service, CancellationToken ct) =>
    service.GetProfile(CurrentUser(context), ct));
api.MapPatch("/me", (HttpContext context, UpdateProfileRequest request, MiniAppService service, CancellationToken ct) =>
    service.UpdateProfile(CurrentUser(context), request, ct));
api.MapGet("/groups", (HttpContext context, MiniAppService service, CancellationToken ct) =>
    service.GetGroups(CurrentUser(context), ct));
api.MapPost("/groups", (HttpContext context, CreateGroupRequest request, MiniAppService service, CancellationToken ct) =>
    service.CreateGroup(CurrentUser(context), request, ct));
api.MapGet("/groups/{groupId:guid}", (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
    service.GetGroup(CurrentUser(context), groupId, ct));
api.MapDelete("/groups/{groupId:guid}", async (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
{
    await service.DeleteGroup(CurrentUser(context), groupId, ct);
    return Results.NoContent();
});
api.MapDelete("/groups/{groupId:guid}/membership", async (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
{
    await service.LeaveGroup(CurrentUser(context), groupId, ct);
    return Results.NoContent();
});
api.MapGet("/groups/{groupId:guid}/participants", (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
    service.GetParticipants(CurrentUser(context), groupId, ct));
api.MapPost("/groups/{groupId:guid}/participants", (HttpContext context, Guid groupId, ParticipantRequest request, MiniAppService service, CancellationToken ct) =>
    service.AddParticipant(CurrentUser(context), groupId, request, ct));
api.MapPatch("/groups/{groupId:guid}/participants/{participantId:long}", (HttpContext context, Guid groupId, long participantId, ParticipantRequest request, MiniAppService service, CancellationToken ct) =>
    service.UpdateParticipant(CurrentUser(context), groupId, participantId, request, ct));
api.MapDelete("/groups/{groupId:guid}/participants/{participantId:long}", async (HttpContext context, Guid groupId, long participantId, MiniAppService service, CancellationToken ct) =>
{
    await service.DeleteParticipant(CurrentUser(context), groupId, participantId, ct);
    return Results.NoContent();
});
api.MapGet("/groups/{groupId:guid}/expenses", (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
    service.GetExpenses(CurrentUser(context), groupId, ct));
api.MapPost("/groups/{groupId:guid}/expenses", (HttpContext context, Guid groupId, ExpenseRequest request, MiniAppService service, CancellationToken ct) =>
    service.CreateExpense(CurrentUser(context), groupId, request, ct));
api.MapGet("/groups/{groupId:guid}/expenses/{expenseId:guid}", (HttpContext context, Guid groupId, Guid expenseId, MiniAppService service, CancellationToken ct) =>
    service.GetExpense(CurrentUser(context), groupId, expenseId, ct));
api.MapPut("/groups/{groupId:guid}/expenses/{expenseId:guid}", (HttpContext context, Guid groupId, Guid expenseId, ExpenseRequest request, MiniAppService service, CancellationToken ct) =>
    service.UpdateExpense(CurrentUser(context), groupId, expenseId, request, ct));
api.MapDelete("/groups/{groupId:guid}/expenses/{expenseId:guid}", async (HttpContext context, Guid groupId, Guid expenseId, MiniAppService service, CancellationToken ct) =>
{
    await service.DeleteExpense(CurrentUser(context), groupId, expenseId, ct);
    return Results.NoContent();
});
api.MapGet("/groups/{groupId:guid}/balances", (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
    service.GetBalances(CurrentUser(context), groupId, ct));
api.MapPost("/groups/{groupId:guid}/transfers", async (HttpContext context, Guid groupId, MarkPaidRequest request, MiniAppService service, CancellationToken ct) =>
{
    await service.MarkPaid(CurrentUser(context), groupId, request, ct);
    return Results.NoContent();
});
api.MapPatch("/groups/{groupId:guid}/transfers/{transferId:guid}", async (HttpContext context, Guid groupId, Guid transferId, ResolveTransferRequest request, MiniAppService service, CancellationToken ct) =>
{
    await service.ResolveTransfer(CurrentUser(context), groupId, transferId, request, ct);
    return Results.NoContent();
});
api.MapPost("/groups/{groupId:guid}/invitations", (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
    service.CreateInvitation(CurrentUser(context), groupId, ct));
api.Map("/{**path}", () => Results.Problem(statusCode: 404, title: "Not found", detail: "API endpoint not found.",
    extensions: new Dictionary<string, object?> { ["code"] = "not_found" }));

app.MapFallbackToFile("index.html");

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

static long CurrentUser(HttpContext context) =>
    ((TelegramMiniAppUser)context.Items[typeof(TelegramMiniAppUser)]!).Id;
