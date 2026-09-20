using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SplitMoneyTg.Application;
using SplitMoneyTg.Domain;
using SplitMoneyTg.Infrastructure;

namespace SplitMoneyTg.Api;

public sealed class ApiIdempotencyMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public async Task InvokeAsync(HttpContext context, AppDbContext db, PostCommitActions postCommitActions, IHostApplicationLifetime lifetime)
    {
        if (!context.Request.Path.StartsWithSegments("/api") || HttpMethods.IsGet(context.Request.Method) ||
            HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method) ||
            !context.Request.Headers.TryGetValue("Idempotency-Key", out var values))
        {
            await next(context);
            return;
        }

        var key = values.ToString().Trim();
        if (key.Length is < 1 or > 100)
            throw new ApiException(400, "Invalid request", "Idempotency-Key must contain between 1 and 100 characters.", "invalid_idempotency_key");

        var userId = ((TelegramMiniAppUser)context.Items[typeof(TelegramMiniAppUser)]!).Id;
        context.Request.EnableBuffering();
        string body;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
            body = await reader.ReadToEndAsync(context.RequestAborted);
        context.Request.Body.Position = 0;

        var fingerprint = string.Join('\n', context.Request.Method, context.Request.Path.Value,
            context.Request.Headers.IfMatch.ToString(), context.Request.Headers["X-Group-Revision"].ToString(), body);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
        var requestHash = Convert.ToHexStringLower(hashBytes);

        if (!db.Database.IsRelational())
        {
            await next(context);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(context.RequestAborted);
        var lockBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{key}"));
        var lockPart1 = BitConverter.ToInt32(lockBytes, 0);
        var lockPart2 = BitConverter.ToInt32(lockBytes, 4);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockPart1}, {lockPart2})", context.RequestAborted);

        var existing = await db.ApiIdempotencyRecords.FindAsync([userId, key], context.RequestAborted);
        if (existing is not null && existing.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            db.ApiIdempotencyRecords.Remove(existing);
            await db.SaveChangesAsync(context.RequestAborted);
            existing = null;
        }
        if (existing is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(existing.RequestHash), hashBytes))
                throw new ApiException(409, "Conflict", "The idempotency key was already used for a different request.", "idempotency_key_reused");
            context.Response.StatusCode = existing.ResponseStatusCode;
            if (existing.ResponseContentType is not null) context.Response.ContentType = existing.ResponseContentType;
            await context.Response.WriteAsync(existing.ResponseBody, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return;
        }

        var originalBody = context.Response.Body;
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        try
        {
            await next(context);
            responseBody.Position = 0;
            var responseText = await new StreamReader(responseBody, Encoding.UTF8).ReadToEndAsync(context.RequestAborted);
            if (context.Response.StatusCode is >= 200 and < 300)
            {
                db.ApiIdempotencyRecords.Add(new ApiIdempotencyRecord
                {
                    UserId = userId,
                    Key = key,
                    RequestHash = requestHash,
                    ResponseStatusCode = context.Response.StatusCode,
                    ResponseContentType = context.Response.ContentType,
                    ResponseBody = responseText,
                    ExpiresAt = DateTimeOffset.UtcNow.Add(Retention)
                });
                await db.SaveChangesAsync(context.RequestAborted);
                await transaction.CommitAsync(context.RequestAborted);
                await postCommitActions.Run(lifetime.ApplicationStopping);
            }
            responseBody.Position = 0;
            await responseBody.CopyToAsync(originalBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}
