using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using Npgsql;
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
builder.Services.AddScoped<PostCommitActions>();
builder.Services.AddScoped<BotHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TelegramInitDataValidator>();
builder.Services.AddSingleton<ITelegramBotClient>(sp => new TelegramBotClient(sp.GetRequiredService<IOptions<TelegramOptions>>().Value.BotToken));
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new LongAsStringJsonConverter());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.Context.Request.Path.StartsWithSegments("/assets"))
            context.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
    }
});
app.UseRouting();
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
            context.Items[typeof(TelegramMiniAppUser)] = user;
        }
        await next(context);
    }
    catch (Exception exception) when (exception is ApiException or BadHttpRequestException or JsonException)
    {
        if (context.Response.HasStarted) throw;
        var apiException = exception as ApiException;
        context.Response.StatusCode = apiException?.StatusCode ?? 400;
        var extensions = apiException?.Extensions is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(apiException.Extensions);
        if (apiException?.Code is { } code) extensions["code"] = code;
        await Results.Problem(
            statusCode: context.Response.StatusCode,
            title: apiException?.Title ?? "Invalid request",
            detail: apiException?.Message ?? "The request body is invalid.",
            extensions: extensions.Count == 0 ? null : extensions)
            .ExecuteAsync(context);
    }
    catch (PostgresException exception) when (context.Request.Path.StartsWithSegments("/api") &&
        exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == "IX_Invitations_OneActivePerCreatorGroup")
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await Results.Problem(statusCode: 409, title: "Conflict", detail: "An active invitation already exists.",
            extensions: new Dictionary<string, object?> { ["code"] = "active_invitation_exists" }).ExecuteAsync(context);
    }
    catch (DbUpdateException exception) when (context.Request.Path.StartsWithSegments("/api") &&
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_Invitations_OneActivePerCreatorGroup"
        })
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await Results.Problem(statusCode: 409, title: "Conflict", detail: "An active invitation already exists.",
            extensions: new Dictionary<string, object?> { ["code"] = "active_invitation_exists" }).ExecuteAsync(context);
    }
    catch (NpgsqlException exception) when (context.Request.Path.StartsWithSegments("/api"))
    {
        if (context.Response.HasStarted) throw;
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Api").LogError(exception, "Database is unavailable");
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await Results.Problem(statusCode: 503, title: "Service unavailable", detail: "The database is temporarily unavailable.",
            extensions: new Dictionary<string, object?> { ["code"] = "database_unavailable" }).ExecuteAsync(context);
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
app.UseMiddleware<ApiIdempotencyMiddleware>();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var user = (TelegramMiniAppUser)context.Items[typeof(TelegramMiniAppUser)]!;
        await context.RequestServices.GetRequiredService<MiniAppService>().UpsertUser(user, context.RequestAborted);
    }
    await next(context);
});

app.MapPost("/telegram/webhook", async (HttpRequest request, Update update, BotHandler handler, IOptions<TelegramOptions> options, CancellationToken ct) =>
{
    if (!request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var secret) || secret != options.Value.WebhookSecret)
        return Results.Unauthorized();
    await handler.Handle(update, ct);
    return Results.Ok();
});
app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
{
    try
    {
        return await db.Database.CanConnectAsync(ct) ? Results.Text("Healthy") : Results.StatusCode(503);
    }
    catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
    {
        return Results.StatusCode(503);
    }
});

var api = app.MapGroup("/api");
api.MapGet("/me", (HttpContext context, MiniAppService service, CancellationToken ct) =>
    service.GetProfile(CurrentUser(context), ct));
api.MapPatch("/me", (HttpContext context, UpdateProfileRequest request, MiniAppService service, CancellationToken ct) =>
    service.UpdateProfile(CurrentUser(context), request, HeaderVersion(context, "If-Match"), ct))
    .WithMetadata(new MutationRequirements(RequiresEntityVersion: true));
api.MapGet("/groups", (HttpContext context, MiniAppService service, CancellationToken ct) =>
    service.GetGroups(CurrentUser(context), ct));
api.MapPost("/groups", (HttpContext context, CreateGroupRequest request, MiniAppService service, CancellationToken ct) =>
    service.CreateGroup(CurrentUser(context), request, ct))
    .WithMetadata(new MutationRequirements());
api.MapGet("/groups/{groupId:guid}", (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
    service.GetGroup(CurrentUser(context), groupId, ct));
api.MapDelete("/groups/{groupId:guid}", async (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
{
    await service.DeleteGroup(CurrentUser(context), groupId, HeaderVersion(context, "X-Group-Revision"), ct);
    return Results.NoContent();
}).WithMetadata(new MutationRequirements(RequiresGroupRevision: true));
api.MapDelete("/groups/{groupId:guid}/membership", async (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
{
    await service.LeaveGroup(CurrentUser(context), groupId, HeaderVersion(context, "X-Group-Revision"), ct);
    return Results.NoContent();
}).WithMetadata(new MutationRequirements(RequiresGroupRevision: true));
api.MapGet("/groups/{groupId:guid}/participants", (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
    service.GetParticipants(CurrentUser(context), groupId, ct));
api.MapPost("/groups/{groupId:guid}/participants", (HttpContext context, Guid groupId, ParticipantRequest request, MiniAppService service, CancellationToken ct) =>
    service.AddParticipant(CurrentUser(context), groupId, request, HeaderVersion(context, "X-Group-Revision"), ct))
    .WithMetadata(new MutationRequirements(RequiresGroupRevision: true));
api.MapPatch("/groups/{groupId:guid}/participants/{participantId:long}", (HttpContext context, Guid groupId, long participantId, ParticipantRequest request, MiniAppService service, CancellationToken ct) =>
    service.UpdateParticipant(CurrentUser(context), groupId, participantId, request, HeaderVersion(context, "If-Match"), HeaderVersion(context, "X-Group-Revision"), ct))
    .WithMetadata(new MutationRequirements(true, true));
api.MapDelete("/groups/{groupId:guid}/participants/{participantId:long}", async (HttpContext context, Guid groupId, long participantId, MiniAppService service, CancellationToken ct) =>
{
    await service.DeleteParticipant(CurrentUser(context), groupId, participantId, HeaderVersion(context, "If-Match"), HeaderVersion(context, "X-Group-Revision"), ct);
    return Results.NoContent();
}).WithMetadata(new MutationRequirements(true, true));
api.MapGet("/groups/{groupId:guid}/expenses", (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
    service.GetExpenses(CurrentUser(context), groupId, ct));
api.MapPost("/groups/{groupId:guid}/expenses", (HttpContext context, Guid groupId, ExpenseRequest request, MiniAppService service, CancellationToken ct) =>
    service.CreateExpense(CurrentUser(context), groupId, request, HeaderVersion(context, "X-Group-Revision"), ct))
    .WithMetadata(new MutationRequirements(RequiresGroupRevision: true));
api.MapGet("/groups/{groupId:guid}/expenses/{expenseId:guid}", (HttpContext context, Guid groupId, Guid expenseId, MiniAppService service, CancellationToken ct) =>
    service.GetExpense(CurrentUser(context), groupId, expenseId, ct));
api.MapPut("/groups/{groupId:guid}/expenses/{expenseId:guid}", (HttpContext context, Guid groupId, Guid expenseId, ExpenseRequest request, MiniAppService service, CancellationToken ct) =>
    service.UpdateExpense(CurrentUser(context), groupId, expenseId, request, HeaderVersion(context, "If-Match"), HeaderVersion(context, "X-Group-Revision"), ct))
    .WithMetadata(new MutationRequirements(true, true));
api.MapDelete("/groups/{groupId:guid}/expenses/{expenseId:guid}", async (HttpContext context, Guid groupId, Guid expenseId, MiniAppService service, CancellationToken ct) =>
{
    await service.DeleteExpense(CurrentUser(context), groupId, expenseId, HeaderVersion(context, "If-Match"), HeaderVersion(context, "X-Group-Revision"), ct);
    return Results.NoContent();
}).WithMetadata(new MutationRequirements(true, true));
api.MapGet("/groups/{groupId:guid}/balances", (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
    service.GetBalances(CurrentUser(context), groupId, ct));
api.MapGet("/groups/{groupId:guid}/balances/details", (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
    service.GetBalanceDetails(CurrentUser(context), groupId, ct));
api.MapPost("/groups/{groupId:guid}/transfers", async (HttpContext context, Guid groupId, MarkPaidRequest request, MiniAppService service, CancellationToken ct) =>
{
    await service.MarkPaid(CurrentUser(context), groupId, request, HeaderVersion(context, "X-Group-Revision"), ct);
    return Results.NoContent();
}).WithMetadata(new MutationRequirements(RequiresGroupRevision: true));
api.MapPatch("/groups/{groupId:guid}/transfers/{transferId:guid}", async (HttpContext context, Guid groupId, Guid transferId, ResolveTransferRequest request, MiniAppService service, CancellationToken ct) =>
{
    await service.ResolveTransfer(CurrentUser(context), groupId, transferId, request, HeaderVersion(context, "If-Match"), HeaderVersion(context, "X-Group-Revision"), ct);
    return Results.NoContent();
}).WithMetadata(new MutationRequirements(true, true));
api.MapGet("/groups/{groupId:guid}/invitations", (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
    service.GetInvitations(CurrentUser(context), groupId, ct));
api.MapPost("/groups/{groupId:guid}/invitations", (HttpContext context, Guid groupId, MiniAppService service, CancellationToken ct) =>
    service.CreateInvitation(CurrentUser(context), groupId, HeaderVersion(context, "X-Group-Revision"), ct))
    .WithMetadata(new MutationRequirements(RequiresGroupRevision: true));
api.MapDelete("/groups/{groupId:guid}/invitations/{invitationId:guid}", async (HttpContext context, Guid groupId, Guid invitationId, MiniAppService service, CancellationToken ct) =>
{
    await service.RevokeInvitation(CurrentUser(context), groupId, invitationId, HeaderVersion(context, "If-Match"), HeaderVersion(context, "X-Group-Revision"), ct);
    return Results.NoContent();
}).WithMetadata(new MutationRequirements(true, true));
api.Map("/{**path}", () => Results.Problem(statusCode: 404, title: "Not found", detail: "API endpoint not found.",
    extensions: new Dictionary<string, object?> { ["code"] = "not_found" }));
app.Map("/assets/{**path}", () => Results.NotFound());

app.MapFallbackToFile("index.html");

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync(
        """
        WITH changed AS (
        INSERT INTO "GroupParticipants"
            ("GroupId", "ParticipantId", "TelegramUserId", "DisplayName", "PaymentDetails", "IsActive", "CreatedAt")
        SELECT gm."GroupId", gm."UserId", gm."UserId", NULL, NULL, gm."IsActive", gm."JoinedAt"
        FROM "GroupMembers" gm
        JOIN "Groups" g ON g."Id" = gm."GroupId"
        WHERE g."Type" = 0
        ON CONFLICT ("GroupId", "ParticipantId") DO UPDATE
        SET "TelegramUserId" = EXCLUDED."TelegramUserId",
            "IsActive" = EXCLUDED."IsActive",
            "Version" = "GroupParticipants"."Version" + 1
        WHERE "GroupParticipants"."TelegramUserId" IS DISTINCT FROM EXCLUDED."TelegramUserId"
           OR "GroupParticipants"."IsActive" IS DISTINCT FROM EXCLUDED."IsActive"
        RETURNING "GroupId")
        UPDATE "Groups" SET "Revision" = "Revision" + 1
        WHERE "Id" IN (SELECT DISTINCT "GroupId" FROM changed);
        """);
    await db.ApiIdempotencyRecords.Where(x => x.ExpiresAt <= DateTimeOffset.UtcNow).ExecuteDeleteAsync();
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

static long? HeaderVersion(HttpContext context, string name)
{
    if (!context.Request.Headers.TryGetValue(name, out var values)) return null;
    var value = values.ToString().Trim();
    if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) value = value[2..].Trim();
    value = value.Trim('"');
    if (!long.TryParse(value, out var version) || version < 1)
        throw new BadHttpRequestException($"{name} must contain a positive integer version.");
    return version;
}

public partial class Program;
