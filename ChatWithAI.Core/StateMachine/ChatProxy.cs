using ChatWithAI.Contracts.Configs;
using ChatWithAI.Contracts.Model;

namespace ChatWithAI.Core.StateMachine
{
    public sealed class ChatProxy : IChat, IDisposable
    {
        private readonly ChatStateMachine stateMachine;
        private readonly Chat impl;

        public string Id { get => impl.Id; }

        public ChatProxy(
            AppConfig config,
            string chatId,
            ChatMode mode,
            IAiAgentFactory aiAgentFactory,
            IMessenger messenger,
            ILogger logger,
            ChatCache cache,
            bool useExpiration)
        {
            cache.Remove($"{chatId}_state");
            impl = new Chat(config, chatId, aiAgentFactory, messenger, logger, cache, useExpiration);
            impl.SetModeAsync(mode).Wait();

            stateMachine = new ChatStateMachine(impl, logger);
        }

        public void Dispose()
        {
            stateMachine.Dispose();
            impl.Dispose();
        }

        public ChatMode GetMode()
        {
            return impl.GetMode();
        }

        public Task SetMode(ChatMode mode)
        {
            return stateMachine.FireAsync(ChatTrigger.UserSetMode, new SetModeContext(mode), CancellationToken.None);
        }

        public Task Reset()
        {
            return stateMachine.FireAsync(ChatTrigger.UserReset, VoidContext.Instance, default);
        }

        public Task AddMessages(List<ChatMessageModel> messages)
        {
            return stateMachine.FireAsync(ChatTrigger.UserAddMessages, new AddMessagesContext(messages), default);
        }

        public Task DoResponseToLastMessage(CancellationToken ct)
        {
            return stateMachine.FireAsync(ChatTrigger.UserRequestResponse, new CancellableContext(ct), ct);
        }

        public Task ContinueLastResponse(CancellationToken ct)
        {
            return stateMachine.FireAsync(ChatTrigger.UserContinue, new CancellableContext(ct), ct);
        }

        public Task RegenerateLastResponse(CancellationToken ct)
        {
            return stateMachine.FireAsync(ChatTrigger.UserRegenerate, new CancellableContext(ct), ct);
        }
    }
}
