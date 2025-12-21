namespace ChatWithAI.Providers.Google
{
    public sealed class GeminiAgentFactory(
        GeminiConfig config,
        IAiImagePainter? aiImagePainter,
        IMemoryStorage memoryStorage,
        IHttpClientFactory httpClientFactory,
        ILogger logger) : IAiAgentFactory
    {
        public IAiAgent CreateAiAgent(
            string aiName,
            string systemMessage,
            bool enableFunctions,
            bool imageEditorMode,
            bool useFlash)
        {
            if (useFlash)
            {
                var newConfig = new GeminiConfig()
                {
                    ApiEndpoint = config.ApiEndpoint,
                    ApiKey = config.ApiKey,
                    MaxTokens = config.MaxTokens,
                    Temperature = config.Temperature,
                    ThinkingLevel = config.ThinkingLevel,
                    MediaResolution = config.MediaResolution,
                    Model = "gemini-3-flash-preview"
                };

                return new GeminiAgent(aiName, systemMessage, enableFunctions, newConfig, aiImagePainter, new GeminiFunctionsManager(memoryStorage, httpClientFactory), httpClientFactory, logger);
            }

            if (imageEditorMode)
            {
                var newConfig = new GeminiConfig()
                {
                    ApiEndpoint = config.ApiEndpoint,
                    ApiKey = config.ApiKey,
                    MaxTokens = config.MaxTokens,
                    Temperature = config.Temperature,
                    ThinkingLevel = "none",
                    MediaResolution = config.MediaResolution,
                    Model = "gemini-3-pro-image-preview"
                };

                return new GeminiAgent(aiName, systemMessage, enableFunctions, newConfig, aiImagePainter, new GeminiFunctionsManager(memoryStorage, httpClientFactory), httpClientFactory, logger);
            }

            return new GeminiAgent(aiName, systemMessage, enableFunctions, config, aiImagePainter, new GeminiFunctionsManager(memoryStorage, httpClientFactory), httpClientFactory, logger);
        }
    }
}