using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SplitMoneyTg.Api;
using Xunit;

namespace SplitMoneyTg.Tests;

public sealed class TelegramInitDataValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    private const string BotToken = "123456:test-token";

    [Fact]
    public void Validate_AcceptsSignedFreshData()
    {
        var validator = new TelegramInitDataValidator(new FixedTimeProvider(Now));

        var user = validator.Validate(CreateInitData(Now), BotToken);

        Assert.Equal(123456789L, user.Id);
        Assert.Equal("Ada Lovelace", user.DisplayName);
        Assert.Equal("ada", user.Username);
    }

    [Fact]
    public void Validate_RejectsTamperedData()
    {
        var validator = new TelegramInitDataValidator(new FixedTimeProvider(Now));
        var initData = CreateInitData(Now).Replace("ada", "eve", StringComparison.Ordinal);

        var error = Assert.Throws<ApiException>(() => validator.Validate(initData, BotToken));

        Assert.Equal(401, error.StatusCode);
        Assert.Equal("invalid_signature", error.Code);
    }

    [Fact]
    public void Validate_RejectsExpiredData()
    {
        var validator = new TelegramInitDataValidator(new FixedTimeProvider(Now));

        var error = Assert.Throws<ApiException>(() => validator.Validate(CreateInitData(Now - TelegramInitDataValidator.MaximumAge - TimeSpan.FromSeconds(1)), BotToken));

        Assert.Equal("expired_init_data", error.Code);
    }

    private static string CreateInitData(DateTimeOffset authDate)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = authDate.ToUnixTimeSeconds().ToString(),
            ["query_id"] = "AAHdF6IQAAAAAN0XohDhrOrc",
            ["user"] = JsonSerializer.Serialize(new { id = 123456789L, first_name = "Ada", last_name = "Lovelace", username = "ada" })
        };
        var check = string.Join('\n', values.Select(x => $"{x.Key}={x.Value}"));
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(BotToken));
        values["hash"] = Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(check))).ToLowerInvariant();
        return string.Join('&', values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
