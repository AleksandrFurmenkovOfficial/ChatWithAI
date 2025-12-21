using ChatWithAI.Contracts.Model;

namespace ChatWithAI.Core.ChatCommands
{
    public sealed class Help(IMessenger messenger) : IChatCommand
    {
        string IChatCommand.Name => "help";
        bool IChatCommand.IsAdminOnlyCommand => false;

        public async Task Execute(IChat chat, ChatMessageModel message, CancellationToken cancellationToken = default)
        {
            await messenger.SendTextMessage(chat.Id, new MessengerMessageDTO { TextContent = Strings.Help }).ConfigureAwait(false);
        }
    }
}