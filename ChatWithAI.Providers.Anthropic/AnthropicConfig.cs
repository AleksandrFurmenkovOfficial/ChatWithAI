using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;

namespace ChatWithAI.Providers.Anthropic
{
    public class AnthropicConfig : IAiProviderConfigBase
    {
        [Required]
        [ConfigurationKeyName("ANTHROPIC_API_KEY")]
        public required string ApiKey { get; set; }

        [Required]
        [ConfigurationKeyName("ANTHROPIC_MODEL")]
        public required string Model { get; set; }

        [Required]
        [ConfigurationKeyName("ANTHROPIC_API_VERSION")]
        public required string ApiVersion { get; set; }

        [Required]
        [ConfigurationKeyName("ANTHROPIC_API_ENDPOINT")]
        public required string ApiEndpoint { get; set; }

        [Required]
        [Range(0.0, 1.0)]
        [ConfigurationKeyName("ANTHROPIC_TEMPERATURE")]
        public double Temperature { get; set; }

        [Required]
        [Range(1, int.MaxValue)]
        [ConfigurationKeyName("ANTHROPIC_MAX_TOKENS")]
        public int MaxTokens { get; set; }
    }
}
