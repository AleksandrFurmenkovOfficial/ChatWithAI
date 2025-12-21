using ChatWithAI.Contracts.Model;
using ChatWithAI.Core.ViewModel;

namespace ChatWithAI.Core
{
    public class ChatState
    {
        public ChatHistoryModel History { get; set; } = null!;
        public ChatUIState UIState { get; set; } = null!;

        public ChatState() { } // For serialization

        public ChatState(string chatId, int maxTextMessageLen, int maxPhotoMessageLen)
        {
            History = new ChatHistoryModel(chatId);
            UIState = new ChatUIState(chatId, maxTextMessageLen, maxPhotoMessageLen);
        }
    }
}
