using System;

namespace ChatWithAI.Contracts
{
    public enum AiProvider
    {
        Anthropic,
        GoogleGemini,
        OpenAI,
        XAI,
        Deepseek
    }

    public static class AiProviderExtensions
    {
        public static AiProvider ToAiProvider(this string provider) => provider?.ToLowerInvariant() switch
        {
            "anthropic" => AiProvider.Anthropic,
            "google" => AiProvider.GoogleGemini,
            "openai" => AiProvider.OpenAI,
            "xai" => AiProvider.XAI,
            "deepseek" => AiProvider.Deepseek,
            _ => throw new ArgumentException($"Unsupported AI provider: {provider}")
        };
    }
}
