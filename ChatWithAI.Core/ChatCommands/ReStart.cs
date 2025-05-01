namespace ChatWithAI.Core.ChatCommands
{
    public sealed class ReStart : IChatCommand
    {
        string IChatCommand.Name => "start";
        bool IChatCommand.IsAdminOnlyCommand => false;

        public async Task Execute(IChat chat, ChatMessage message, CancellationToken cancellationToken = default)
        {
            await Execute(chat, cancellationToken).ConfigureAwait(false);
        }

        public static async Task Execute(IChat chat, CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2016 // Forward the 'CancellationToken' parameter to methods
            string modeNameFull = (string)chat.GetMode().AiName.Split("_")[1].Clone();
            var mode = modeNameFull.Replace("Mode", "");
            await chat.Reset().ConfigureAwait(false);
#pragma warning disable CA1863 // Use 'CompositeFormat'
            var msg = string.Format(CultureInfo.InvariantCulture, Strings.StartWarning, mode);
#pragma warning restore CA1863 // Use 'CompositeFormat'
            await chat.SendSystemMessage(msg).ConfigureAwait(false);
#pragma warning restore CA2016 // Forward the 'CancellationToken' parameter to methods
        }
    }
}