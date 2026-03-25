using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;


namespace ChatWithAI.Contracts.Configs
{
    public class AppConfig
    {
        [Required]
        [ConfigurationKeyName("AI_PROVIDER")]
        public string? Provider { get; set; }

        [ConfigurationKeyName("CHAT_CACHE_ALIVE_IN_MINUTES")]
        public int ChatCacheAliveInMinutes { get; set; } = 5;

        [ConfigurationKeyName("BASE_MODE")]
        public string BaseMode { get; set; } = "common";
    }
}
