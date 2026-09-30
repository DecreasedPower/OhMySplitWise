using Microsoft.EntityFrameworkCore;
using SplitMoneyTg.Infrastructure;

namespace SplitMoneyTg.Application;

public sealed class UsageMetricsService(AppDbContext db, TimeProvider timeProvider)
{
    public async Task RecordActivity(long userId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        if (db.Database.IsRelational())
        {
            // Concurrent requests must never move the timestamp backwards or change the profile version.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Users" SET "LastActiveAt" = {now}
                WHERE "TelegramId" = {userId} AND ("LastActiveAt" IS NULL OR "LastActiveAt" < {now})
                """, ct);
            return;
        }

        var user = await db.Users.FindAsync([userId], ct);
        if (user is not null && (user.LastActiveAt is null || user.LastActiveAt < now))
        {
            user.LastActiveAt = now;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<UsageMetrics> GetMetrics(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var weekStart = now.AddDays(-7);
        var monthStart = now.AddDays(-30);
        return new UsageMetrics(
            await db.Groups.LongCountAsync(ct),
            await db.Users.LongCountAsync(x => x.LastActiveAt >= weekStart && x.LastActiveAt <= now, ct),
            await db.Users.LongCountAsync(x => x.LastActiveAt >= monthStart && x.LastActiveAt <= now, ct),
            now);
    }
}

public sealed record UsageMetrics(long TotalGroupsCreated, long ActiveUsersLast7Days, long ActiveUsersLast30Days,
    DateTimeOffset AsOf);
