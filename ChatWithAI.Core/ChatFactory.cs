using ChatWithAI.Contracts.Configs;

namespace ChatWithAI.Core
{
    public sealed class ChatFactory(AppConfig config, IChatModeLoader modeLoader, IAiAgentFactory aIAgentFactory, IMessenger messenger, ILogger logger, ChatCache cache) : IChatFactory
    {
        public async Task<IChat> CreateChat(string chatId, string modeName, bool useExpiration)
        {
            var chat = new Chat(config, chatId, aIAgentFactory, messenger, logger, cache, useExpiration);
            var mode = await modeLoader.GetChatMode(modeName).ConfigureAwait(false);
            chat.SetMode(mode);
            return chat;
        }
    }
}