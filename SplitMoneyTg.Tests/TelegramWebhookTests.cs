using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Testcontainers.PostgreSql;
using Xunit;

namespace SplitMoneyTg.Tests;

public sealed class TelegramWebhookTests
{
    [Fact]
    public async Task Stats_AcceptsTelegramCommandJsonAndDeduplicatesDelivery()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        using var transport = new TelegramTransport();
        using var http = new HttpClient(transport);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", postgres.GetConnectionString());
            builder.UseSetting("Telegram:BotToken", "123456:test-token");
            builder.UseSetting("Telegram:WebhookSecret", "test-secret");
            builder.UseSetting("Telegram:WebhookUrl", "");
            builder.UseSetting("Telegram:StatsAdminIds", "368900896");
            builder.ConfigureServices(services => services.AddSingleton<ITelegramBotClient>(
                new TelegramBotClient("123456:test-token", http)));
        });
        using var client = factory.CreateClient();
        const string payload = """
            {"update_id":12345,"message":{"message_id":10,"from":{"id":368900896,"is_bot":false,"first_name":"Admin"},
            "chat":{"id":368900896,"type":"private","first_name":"Admin"},"date":1790807800,
            "text":"/stats","entities":[{"offset":0,"length":6,"type":"bot_command"}]}}
            """;

        using (var unauthorized = await client.PostAsync("/telegram/webhook",
            new StringContent(payload, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        for (var i = 0; i < 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/telegram/webhook")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", "test-secret");
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Single(transport.Messages);
        Assert.Contains("Всего групп создано: 0", transport.Messages[0]);
        Assert.Contains("Активных пользователей за 7 суток: 1", transport.Messages[0]);
    }

    private sealed class TelegramTransport : HttpMessageHandler
    {
        public List<string> Messages { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Messages.Add(body.RootElement.GetProperty("text").GetString()!);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"ok":true,"result":{"message_id":1,"date":1790807800,"chat":{"id":368900896,"type":"private"},"text":"test"}}
                    """, Encoding.UTF8, "application/json")
            };
        }
    }
}
