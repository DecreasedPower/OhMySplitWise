namespace SplitMoneyTg.Telegram;

public sealed class TelegramOptions
{
    public const string Section = "Telegram";
    public string BotToken { get; set; } = "";
    public string WebhookUrl { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
}
