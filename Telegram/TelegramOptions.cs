namespace SplitMoneyTg.Telegram;

public sealed class TelegramOptions
{
    public const string Section = "Telegram";
    public string BotToken { get; set; } = "";
    public string WebhookUrl { get; set; } = "";
    public string MiniAppUrl { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string StatsAdminIds { get; set; } = "";

    public bool CanViewStats(long userId) => userId > 0 && StatsAdminIds
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(value => long.TryParse(value, out var adminId) && adminId == userId);
}
