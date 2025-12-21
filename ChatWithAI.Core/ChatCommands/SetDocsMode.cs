using ChatWithAI.Contracts.Model;

namespace ChatWithAI.Core.ChatCommands
{
    public sealed class SetDocsMode(IChatModeLoader modeLoader, IMessenger messenger) : IChatCommand
    {
        public static string StaticName => "docs";
        public string Name => StaticName;
        bool IChatCommand.IsAdminOnlyCommand => false;

        private readonly IChatModeLoader modeLoader = modeLoader;

        public async Task Execute(IChat chat, ChatMessageModel message, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return;

            var mode = await modeLoader.GetChatMode(Name, cancellationToken).ConfigureAwait(false);
            mode.UseFunctions = true;
            mode.UseImage = false;
            mode.UseFlash = true;

            await chat.SetMode(mode).ConfigureAwait(false);
            await chat.Reset().ConfigureAwait(false);

            await messenger.SendTextMessage(chat.Id, new MessengerMessageDTO { TextContent = Strings.DocsModeNow }).ConfigureAwait(false);
        }
    }
}