using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;

namespace ChatWithAI.Providers.Google
{
    public class GeminiConfig : IAiProviderConfigBase
    {
        [Required]
        [ConfigurationKeyName("GOOGLE_API_KEY")]
        public required string ApiKey { get; set; }

        [Required]
        [ConfigurationKeyName("GOOGLE_MODEL")]
        public required string Model { get; set; } = "gemini-3-flash-preview";

        [Required]
        [ConfigurationKeyName("GOOGLE_API_ENDPOINT")]
        public required string ApiEndpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

        [Required]
        [Range(0.0, 1.0)]
        [ConfigurationKeyName("GOOGLE_TEMPERATURE")]
        public double Temperature { get; set; } = 1.0;

        [Required]
        [Range(1, int.MaxValue)]
        [ConfigurationKeyName("GOOGLE_MAX_TOKENS")]
        public int MaxTokens { get; set; }

        [ConfigurationKeyName("GOOGLE_THINKING_LEVEL")]
        public string ThinkingLevel { get; set; } = "low";

        [ConfigurationKeyName("GOOGLE_MEDIA_RESOLUTION")]
        public string MediaResolution { get; set; } = "MEDIA_RESOLUTION_MEDIUM";
    }
}