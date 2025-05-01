namespace ChatWithAI.Core.ChatCommands
{
    public sealed class SetCommonMode(IChatModeLoader modeLoader) : IChatCommand
    {
        public string Name => "common";
        bool IChatCommand.IsAdminOnlyCommand => false;

        readonly IChatModeLoader modeLoader = modeLoader;

        public async Task Execute(IChat chat, ChatMessage message, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return;

            var mode = await modeLoader.GetChatMode(Name, cancellationToken).ConfigureAwait(false);
            chat.SetMode(mode);
            await chat.SendSystemMessage(Strings.CommonModeNow, cancellationToken).ConfigureAwait(false);
        }
    }
}