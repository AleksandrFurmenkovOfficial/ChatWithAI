using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;

namespace ChatWithAI.Core
{
    public class TelegramConfig
    {
        [Required]
        [ConfigurationKeyName("TELEGRAM_BOT_KEY")]
        public required string BotToken { get; set; }

        [ConfigurationKeyName("TELEGRAM_ADMIN_USER_ID")]
        public string? AdminUserId { get; set; }

        [ConfigurationKeyName("TELEGRAM_MESSAGE_MAX_LEN")]
        [Range(1, int.MaxValue)]
        public int MessageLengthLimit { get; set; } = 4096;

        [ConfigurationKeyName("TELEGRAM_CAPTION_MAX_LEN")]
        [Range(1, int.MaxValue)]
        public int PhotoMessageLengthLimit { get; set; } = 1024;
    }
}
