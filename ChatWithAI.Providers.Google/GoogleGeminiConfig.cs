using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;

namespace ChatWithAI.Providers.Google
{
    public class GoogleGeminiConfig : IAiProviderConfigBase
    {
        [Required]
        [ConfigurationKeyName("GOOGLE_API_KEY")]
        public required string ApiKey { get; set; }

        [Required]
        [ConfigurationKeyName("GOOGLE_MODEL")]
        public required string Model { get; set; }

        [Required]
        [ConfigurationKeyName("GOOGLE_API_ENDPOINT")]
        public required string ApiEndpoint { get; set; }

        [Required]
        [Range(0.0, 1.0)]
        [ConfigurationKeyName("GOOGLE_TEMPERATURE")]
        public double Temperature { get; set; }

        [Required]
        [Range(1, int.MaxValue)]
        [ConfigurationKeyName("GOOGLE_MAX_TOKENS")]
        public int MaxTokens { get; set; }
    }
}