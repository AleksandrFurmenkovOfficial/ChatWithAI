namespace ChatWithAI.Core.ChatCommands
{
    public sealed class ClearDiary(IMemoryStorage memoryStorage) : IChatCommand
    {
        string IChatCommand.Name => "clear";
        bool IChatCommand.IsAdminOnlyCommand => false;

        public async Task Execute(IChat chat, ChatMessage message, CancellationToken cancellationToken = default)
        {
            var mode = chat.GetMode();
            if (mode != null)
            {
                memoryStorage.Remove(mode.AiName, chat.Id, cancellationToken);
            }

            await chat.SendSystemMessage("Diary has been cleared", cancellationToken).ConfigureAwait(false);
        }
    }
}