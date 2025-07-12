namespace ChatWithAI.Core
{
    public sealed class ChatModeLoader(IModeStorage modeStorage) : IChatModeLoader
    {
        public async Task<ChatMode> GetChatMode(string modeName, CancellationToken cancellationToken = default)
        {
            var systemMessage = await modeStorage.GetContent(modeName, cancellationToken).ConfigureAwait(false);
            systemMessage += "\nТелеграм в котором сейчас проходит сеанс связи не поддерживает таблицы. Использования таблиц следует избегать.";
            return new ChatMode
            {
                AiName = $"Vivy_{modeName}",
                AiSettings = systemMessage
            };
        }
    }
}