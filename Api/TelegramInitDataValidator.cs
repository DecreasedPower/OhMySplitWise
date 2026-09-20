using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SplitMoneyTg.Api;

public sealed record TelegramMiniAppUser(long Id, string FirstName, string? LastName, string? Username)
{
    public string DisplayName => string.Join(' ', new[] { FirstName, LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed class TelegramInitDataValidator(TimeProvider timeProvider)
{
    public static readonly TimeSpan MaximumAge = TimeSpan.FromHours(1);

    public TelegramMiniAppUser Validate(string initData, string botToken)
    {
        if (string.IsNullOrWhiteSpace(initData)) throw Unauthorized("Missing Telegram init data.", "missing_init_data");

        Dictionary<string, string> values;
        try
        {
            values = initData.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .ToDictionary(part => Uri.UnescapeDataString(part[0].Replace('+', ' ')),
                    part => Uri.UnescapeDataString((part.Length == 2 ? part[1] : "").Replace('+', ' ')), StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException)
        {
            throw Unauthorized("Malformed Telegram init data.", "invalid_init_data");
        }

        if (!values.Remove("hash", out var suppliedHash) || suppliedHash.Length != 64 ||
            !values.TryGetValue("auth_date", out var authDateText) ||
            !long.TryParse(authDateText, NumberStyles.None, CultureInfo.InvariantCulture, out var authDateSeconds))
            throw Unauthorized("Malformed Telegram init data.", "invalid_init_data");

        var dataCheckString = string.Join('\n', values.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}"));
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));
        var expectedHash = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(dataCheckString));
        byte[] suppliedHashBytes;
        try { suppliedHashBytes = Convert.FromHexString(suppliedHash); }
        catch (FormatException) { throw Unauthorized("Telegram signature is invalid.", "invalid_signature"); }
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHashBytes))
            throw Unauthorized("Telegram signature is invalid.", "invalid_signature");

        DateTimeOffset authDate;
        try { authDate = DateTimeOffset.FromUnixTimeSeconds(authDateSeconds); }
        catch (ArgumentOutOfRangeException) { throw Unauthorized("Telegram init data has an invalid timestamp.", "invalid_init_data"); }
        var age = timeProvider.GetUtcNow() - authDate;
        if (age < TimeSpan.FromMinutes(-1) || age > MaximumAge)
            throw Unauthorized("Telegram init data has expired.", "expired_init_data");

        if (!values.TryGetValue("user", out var userJson))
            throw Unauthorized("Telegram user data is missing.", "invalid_init_data");
        try
        {
            var user = JsonSerializer.Deserialize<UserPayload>(userJson);
            if (user is null || user.Id == 0 || string.IsNullOrWhiteSpace(user.FirstName)) throw new JsonException();
            return new TelegramMiniAppUser(user.Id, user.FirstName, user.LastName, user.Username);
        }
        catch (JsonException)
        {
            throw Unauthorized("Telegram user data is invalid.", "invalid_init_data");
        }
    }

    private static ApiException Unauthorized(string detail, string code) => new(401, "Unauthorized", detail, code);

    private sealed record UserPayload(
        [property: System.Text.Json.Serialization.JsonPropertyName("id")] long Id,
        [property: System.Text.Json.Serialization.JsonPropertyName("first_name")] string FirstName,
        [property: System.Text.Json.Serialization.JsonPropertyName("last_name")] string? LastName,
        [property: System.Text.Json.Serialization.JsonPropertyName("username")] string? Username);
}
