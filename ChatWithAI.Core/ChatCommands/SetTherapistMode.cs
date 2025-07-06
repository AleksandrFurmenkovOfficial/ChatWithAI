namespace ChatWithAI.Core.ChatCommands
{
    public sealed class SetTherapistMode(IChatModeLoader modeLoader) : IChatCommand
    {
        public string Name => "therapist";
        bool IChatCommand.IsAdminOnlyCommand => false;

        readonly IChatModeLoader modeLoader = modeLoader;

        public async Task Execute(IChat chat, ChatMessage message, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return;

            var mode = await modeLoader.GetChatMode(Name, cancellationToken).ConfigureAwait(false);
            chat.SetMode(mode);
            await chat.SendSystemMessage(Strings.TherapistModeNow, cancellationToken).ConfigureAwait(false);
        }
    }
}