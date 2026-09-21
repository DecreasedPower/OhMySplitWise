using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SplitMoneyTg.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace SplitMoneyTg.Tests;

public sealed class ApiProtocolTests
{
    private const string BotToken = "123456:test-token";
    private const string GroupId = "10000000-0000-0000-0000-000000000001";
    private const string EntityId = "20000000-0000-0000-0000-000000000001";

    [Fact]
    public async Task Mutations_EnforceProtocolAndPreconditions_AndReplaySuccessfulResponse()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"splitmoney-web-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(webRoot, "assets"));
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<html>test</html>", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "assets", "app-hash.js"), "window.test = true;", TestContext.Current.CancellationToken);
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseWebRoot(webRoot);
            builder.UseSetting("ConnectionStrings:Postgres", postgres.GetConnectionString());
            builder.UseSetting("Telegram:BotToken", BotToken);
            builder.UseSetting("Telegram:WebhookSecret", "test-secret");
            builder.UseSetting("Telegram:WebhookUrl", "");
        });
        using var client = factory.CreateClient();
        var authorization = $"tma {CreateInitData()}";

        foreach (var mutation in Mutations)
        {
            using var response = await Send(client, mutation, authorization);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.StatusCode == HttpStatusCode.UpgradeRequired,
                $"{mutation.Method} {mutation.Path} returned {(int)response.StatusCode} instead of 426: {responseBody}");
            AssertProblem(responseBody, response.StatusCode, HttpStatusCode.UpgradeRequired, "client_upgrade_required");
            AssertNoStore(response);
        }

        foreach (var mutation in Mutations)
        {
            using var response = await Send(client, mutation, authorization, protocol: true);
            await AssertProblem(response, (HttpStatusCode)428, "idempotency_key_required");
        }

        foreach (var mutation in Mutations.Where(x => x.RequiresEntityVersion || x.RequiresGroupRevision))
        {
            using var response = await Send(client, mutation, authorization, protocol: true, idempotency: true,
                entityVersion: mutation.RequiresEntityVersion ? false : null);
            await AssertProblem(response, (HttpStatusCode)428,
                mutation.RequiresEntityVersion ? "entity_version_required" : "group_revision_required");

            if (mutation.RequiresEntityVersion && mutation.RequiresGroupRevision)
            {
                using var groupResponse = await Send(client, mutation, authorization, protocol: true, idempotency: true,
                    entityVersion: true, groupRevision: false);
                await AssertProblem(groupResponse, (HttpStatusCode)428, "group_revision_required");
            }
        }

        using (var outdated = await Send(client, Mutations[0], authorization, protocol: true, protocolValue: "1"))
            await AssertProblem(outdated, HttpStatusCode.UpgradeRequired, "client_upgrade_required");
        using (var invalidKey = await Send(client, Mutations[1], authorization, protocol: true, idempotency: true,
            idempotencyKey: new string('x', 101)))
            await AssertProblem(invalidKey, HttpStatusCode.BadRequest, "invalid_idempotency_key");
        using (var invalidVersion = await Send(client, Mutations[0], authorization, protocol: true, idempotency: true,
            entityVersion: true, entityVersionValue: "invalid"))
            Assert.Equal(HttpStatusCode.BadRequest, invalidVersion.StatusCode);
        using (var invalidRevision = await Send(client, Mutations[2], authorization, protocol: true, idempotency: true,
            groupRevision: true, groupRevisionValue: "invalid"))
            Assert.Equal(HttpStatusCode.BadRequest, invalidRevision.StatusCode);

        await using (var rejectedScope = factory.Services.CreateAsyncScope())
        {
            var rejectedDb = rejectedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await rejectedDb.Users.CountAsync(TestContext.Current.CancellationToken));
        }

        using (var get = new HttpRequestMessage(HttpMethod.Get, "/api/me"))
        {
            get.Headers.TryAddWithoutValidation("Authorization", authorization);
            using var response = await client.SendAsync(get, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertNoStore(response);
        }

        const string key = "create-group-command";
        using var first = await CreateGroup(client, authorization, key, "First");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        AssertNoStore(first);

        using var replay = await CreateGroup(client, authorization, key, "First");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(firstBody, await replay.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        AssertNoStore(replay);

        using var changed = await CreateGroup(client, authorization, key, "Changed");
        await AssertProblem(changed, HttpStatusCode.Conflict, "idempotency_key_reused");

        using var missingAsset = await client.GetAsync("/assets/does-not-exist.js", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missingAsset.StatusCode);
        AssertNoStore(missingAsset);
        using var existingAsset = await client.GetAsync("/assets/app-hash.js", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, existingAsset.StatusCode);
        Assert.Equal("window.test = true;", await existingAsset.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("immutable", existingAsset.Headers.CacheControl?.ToString());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Groups.CountAsync(TestContext.Current.CancellationToken));
        Directory.Delete(webRoot, recursive: true);
    }

    private static readonly Mutation[] Mutations =
    [
        new(HttpMethod.Patch, "/api/me", true, false),
        new(HttpMethod.Post, "/api/groups", false, false),
        new(HttpMethod.Delete, $"/api/groups/{GroupId}", false, true),
        new(HttpMethod.Delete, $"/api/groups/{GroupId}/membership", false, true),
        new(HttpMethod.Post, $"/api/groups/{GroupId}/participants", false, true),
        new(HttpMethod.Patch, $"/api/groups/{GroupId}/participants/1", true, true),
        new(HttpMethod.Delete, $"/api/groups/{GroupId}/participants/1", true, true),
        new(HttpMethod.Post, $"/api/groups/{GroupId}/expenses", false, true),
        new(HttpMethod.Put, $"/api/groups/{GroupId}/expenses/{EntityId}", true, true),
        new(HttpMethod.Delete, $"/api/groups/{GroupId}/expenses/{EntityId}", true, true),
        new(HttpMethod.Post, $"/api/groups/{GroupId}/transfers", false, true),
        new(HttpMethod.Patch, $"/api/groups/{GroupId}/transfers/{EntityId}", true, true),
        new(HttpMethod.Post, $"/api/groups/{GroupId}/invitations", false, true),
        new(HttpMethod.Delete, $"/api/groups/{GroupId}/invitations/{EntityId}", true, true)
    ];

    private static async Task<HttpResponseMessage> Send(HttpClient client, Mutation mutation, string authorization,
        bool protocol = false, bool idempotency = false, bool? entityVersion = null, bool? groupRevision = null,
        string protocolValue = "2", string? idempotencyKey = null, string entityVersionValue = "1", string groupRevisionValue = "1")
    {
        var request = new HttpRequestMessage(mutation.Method, mutation.Path)
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        if (protocol) request.Headers.TryAddWithoutValidation("X-Client-Protocol", protocolValue);
        if (idempotency) request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("N"));
        if (entityVersion == true) request.Headers.TryAddWithoutValidation("If-Match", entityVersionValue);
        if (groupRevision == true) request.Headers.TryAddWithoutValidation("X-Group-Revision", groupRevisionValue);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> CreateGroup(HttpClient client, string authorization, string key, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/groups")
        {
            Content = JsonContent.Create(new { name, type = "collective" })
        };
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.TryAddWithoutValidation("X-Client-Protocol", "2");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        AssertProblem(body, response.StatusCode, status, code);
    }

    private static void AssertProblem(string body, HttpStatusCode actualStatus, HttpStatusCode status, string code)
    {
        Assert.Equal(status, actualStatus);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());

    private static string CreateInitData()
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["query_id"] = "release-two-test",
            ["user"] = JsonSerializer.Serialize(new { id = 123456789L, first_name = "Ada", username = "ada" })
        };
        var check = string.Join('\n', values.Select(x => $"{x.Key}={x.Value}"));
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(BotToken));
        values["hash"] = Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(check))).ToLowerInvariant();
        return string.Join('&', values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    private sealed record Mutation(HttpMethod Method, string Path, bool RequiresEntityVersion, bool RequiresGroupRevision);
}
