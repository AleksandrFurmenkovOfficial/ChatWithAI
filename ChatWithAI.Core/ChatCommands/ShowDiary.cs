using ChatWithAI.Contracts.Model;

namespace ChatWithAI.Core.ChatCommands
{
    public sealed class ShowDiary(IMemoryStorage memoryStorage, IMessenger messenger) : IChatCommand
    {
        string IChatCommand.Name => "showdiary";
        bool IChatCommand.IsAdminOnlyCommand => true;

        public async Task Execute(IChat chat, ChatMessageModel message, CancellationToken cancellationToken = default)
        {
            var diary = await memoryStorage.GetContent(chat.Id, chat.GetMode().AiName, cancellationToken).ConfigureAwait(false);
            diary = string.IsNullOrEmpty(diary) ? Strings.DiaryEmpty : diary;
            await messenger.SendTextMessage(chat.Id, new MessengerMessageDTO { TextContent = diary }).ConfigureAwait(false);
        }
    }
}