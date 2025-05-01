namespace ChatWithAI.Core
{
    public sealed class ChatMessageProcessor(IChatCommandProcessor chatCommandProcessor) : IChatMessageProcessor
    {
        public async Task HandleMessage(IChat chat, ChatMessage message, CancellationToken cancellationToken = default)
        {
            bool isCommandDone = await chatCommandProcessor.ExecuteIfChatCommand(chat, message, cancellationToken)
                .ConfigureAwait(false);
            if (isCommandDone)
            {
                return;
            }

            await chat.DoResponseToMessage(message, cancellationToken).ConfigureAwait(false);
        }
    }
}