using ChatWithAI.Contracts.Model;

namespace ChatWithAI.Core.ChatCommands
{
    public sealed class ClearDiary(IMemoryStorage memoryStorage, IMessenger messenger) : IChatCommand
    {
        string IChatCommand.Name => "clear";
        bool IChatCommand.IsAdminOnlyCommand => false;

        public async Task Execute(IChat chat, ChatMessageModel message, CancellationToken cancellationToken = default)
        {
            var mode = chat.GetMode();
            if (mode != null)
            {
                memoryStorage.Remove(chat.Id, mode.AiName, default);
            }

            await messenger.SendTextMessage(chat.Id, new MessengerMessageDTO { TextContent = Strings.DiaryCleared }).ConfigureAwait(false);
        }
    }
}