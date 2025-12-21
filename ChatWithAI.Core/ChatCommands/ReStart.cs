using ChatWithAI.Contracts.Model;

namespace ChatWithAI.Core.ChatCommands
{
    public sealed class ReStart(IChatModeLoader modeLoader) : IChatCommand
    {
        string IChatCommand.Name => "start";
        bool IChatCommand.IsAdminOnlyCommand => false;

        public async Task Execute(IChat chat, ChatMessageModel message, CancellationToken cancellationToken = default)
        {
            var mode = await modeLoader.GetChatMode(SetCommonMode.StaticName, default).ConfigureAwait(false);
            mode.UseFunctions = true;
            mode.UseImage = false;
            mode.UseFlash = false;

            await chat.SetMode(mode).ConfigureAwait(false);
            await chat.Reset().ConfigureAwait(false);
        }
    }
}