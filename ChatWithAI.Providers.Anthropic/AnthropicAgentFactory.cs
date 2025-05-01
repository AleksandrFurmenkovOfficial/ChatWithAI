namespace ChatWithAI.Providers.Anthropic
{
    public sealed class AnthropicAgentFactory(
        AnthropicConfig config,
        IAiImagePainter? aiImagePainter,
        IMemoryStorage memoryStorage) : IAiAgentFactory
    {
        public IAiAgent CreateAiAgent(
            string aiName,
            string systemMessage,
            bool enableFunctions)
        {
            return new AnthropicAgent(aiName, systemMessage, enableFunctions, config, aiImagePainter, new AnthropicFunctionsManager(memoryStorage));
        }
    }
}