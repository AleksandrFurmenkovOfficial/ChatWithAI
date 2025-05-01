namespace ChatWithAI.Providers.Google
{
    public sealed class GoogleGeminiAgentFactory(
        GoogleGeminiConfig config,
        IAiImagePainter? aiImagePainter,
        IMemoryStorage memoryStorage) : IAiAgentFactory
    {
        public IAiAgent CreateAiAgent(
            string aiName,
            string systemMessage,
            bool enableFunctions)
        {
            return new GoogleGeminiAgent(aiName, systemMessage, enableFunctions, config, aiImagePainter, new GoogleGeminiFunctionsManager(memoryStorage));
        }
    }
}