using ChatWithAI.Contracts.Model;

namespace ChatWithAI.Core.ChatCommands
{
    public sealed class SetPhotoEditorMode(IChatModeLoader modeLoader, IMessenger messenger) : IChatCommand
    {
        public static string StaticName => "photoeditor";
        public string Name => StaticName;
        bool IChatCommand.IsAdminOnlyCommand => false;

        private readonly IChatModeLoader modeLoader = modeLoader;

        public async Task Execute(IChat chat, ChatMessageModel message, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return;

            var mode = await modeLoader.GetChatMode(Name, cancellationToken).ConfigureAwait(false);
            mode.UseFunctions = false;
            mode.UseImage = true;
            mode.UseFlash = false;

            await chat.SetMode(mode).ConfigureAwait(false);
            await chat.Reset().ConfigureAwait(false);

            await messenger.SendTextMessage(chat.Id, new MessengerMessageDTO { TextContent = Strings.PhotoEditorModeNow }).ConfigureAwait(false);
        }
    }
}