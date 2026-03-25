namespace ChatWithAI.Contracts
{
    public interface IAiAgentFactory
    {
        IAiAgent CreateAiAgent(
            string aiName,
            string systemMessage,
            bool enableFunctions,
            bool imageEditorMode,
            bool useFlash);
    }
}