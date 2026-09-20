using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SplitMoneyTg.Api;
using SplitMoneyTg.Application;
using SplitMoneyTg.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace SplitMoneyTg.Tests;

public sealed class PostgresSchemaTests
{
    [Fact]
    public async Task HardeningMigration_EnforcesFinancialInvariants()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260814145005_AddStandaloneGroups", TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await Execute(connection,
            """
            INSERT INTO "Users" ("TelegramId", "DisplayName", "CreatedAt") VALUES (1, 'One', now()), (2, 'Two', now());
            INSERT INTO "Groups" ("Id", "Name", "OwnerId", "Type", "IsArchived", "CreatedAt")
            VALUES ('10000000-0000-0000-0000-000000000001', 'Test', 1, 0, false, now());
            INSERT INTO "GroupMembers" ("GroupId", "UserId", "IsActive", "JoinedAt")
            VALUES ('10000000-0000-0000-0000-000000000001', 1, true, now()),
                   ('10000000-0000-0000-0000-000000000001', 2, true, now());
            INSERT INTO "GroupParticipants" ("GroupId", "ParticipantId", "TelegramUserId", "IsActive", "CreatedAt")
            VALUES ('10000000-0000-0000-0000-000000000001', 1, 1, true, now()),
                   ('10000000-0000-0000-0000-000000000001', 2, 2, true, now());
            INSERT INTO "Invitations" ("Id", "Token", "GroupId", "CreatedById", "IsActive", "CreatedAt")
            VALUES ('40000000-0000-0000-0000-000000000001', '11111111111111111111111111111111', '10000000-0000-0000-0000-000000000001', 1, true, now());
            INSERT INTO "Expenses" ("Id", "GroupId", "AuthorId", "PayerId", "Description", "AmountKopecks", "CreatedAt")
            VALUES ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 1, 1, 'Lunch', 100, now());
            INSERT INTO "ExpenseShares" ("ExpenseId", "UserId", "AmountKopecks")
            VALUES ('20000000-0000-0000-0000-000000000001', 1, 100);
            """, TestContext.Current.CancellationToken);
        await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);

        await using (var command = new NpgsqlCommand(
            "SELECT \"GroupId\" FROM \"ExpenseShares\" WHERE \"ExpenseId\" = '20000000-0000-0000-0000-000000000001'", connection))
            Assert.Equal(Guid.Parse("10000000-0000-0000-0000-000000000001"), await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        await using (var command = new NpgsqlCommand(
            "SELECT NOT \"IsActive\" AND \"RevokedAt\" IS NOT NULL FROM \"Invitations\" WHERE \"Id\" = '40000000-0000-0000-0000-000000000001'", connection))
            Assert.Equal(true, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        await Execute(connection,
            """
            INSERT INTO "Transfers" ("Id", "GroupId", "FromUserId", "ToUserId", "AmountKopecks", "Status", "CreatedAt")
            VALUES ('30000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 1, 2, 100, 0, now());
            """, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<PostgresException>(() => Execute(connection,
            """
            INSERT INTO "Transfers" ("Id", "GroupId", "FromUserId", "ToUserId", "AmountKopecks", "Status", "CreatedAt")
            VALUES ('30000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', 1, 2, 100, 0, now());
            """, TestContext.Current.CancellationToken));

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var firstDb = new AppDbContext(options);
        await using var secondDb = new AppDbContext(options);
        var middleware = new ApiIdempotencyMiddleware(async context =>
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });
        var first = InvokeIdempotent(middleware, firstDb, "{\"value\":1}");
        var second = InvokeIdempotent(middleware, secondDb, "{\"value\":2}");
        var outcomes = await Task.WhenAll(first, second);
        Assert.Single(outcomes, x => x is null);
        var conflict = Assert.Single(outcomes, x => x is not null);
        Assert.Equal("idempotency_key_reused", Assert.IsType<ApiException>(conflict).Code);
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<Exception?> InvokeIdempotent(ApiIdempotencyMiddleware middleware, AppDbContext db, string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/test";
        context.Request.Headers["Idempotency-Key"] = "same-key";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        context.Items[typeof(TelegramMiniAppUser)] = new TelegramMiniAppUser(1, "One", null, null);
        try
        {
            await middleware.InvokeAsync(context, db, new PostCommitActions(), new TestHostApplicationLifetime());
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
