using ChatWithAI.Contracts.Configs;
using ChatWithAI.Core.StateMachine;

namespace ChatWithAI.Core
{
    public sealed class ChatFactory(AppConfig config, IAiAgentFactory aIAgentFactory, IMessenger messenger, ILogger logger, ChatCache cache) : IChatFactory
    {
        public async Task<IChat> CreateChat(string chatId, ChatMode mode, bool useExpiration)
        {
            return new ChatProxy(config, chatId, mode, aIAgentFactory, messenger, logger, cache, useExpiration);
        }
    }
}