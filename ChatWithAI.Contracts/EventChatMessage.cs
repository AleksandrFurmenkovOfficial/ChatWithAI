using ChatWithAI.Contracts.Model;

namespace ChatWithAI.Contracts
{
    public class EventChatMessage(string chatId, string orderId, string username, ChatMessageModel chatMessage) : IChatEvent
    {
        public string ChatId => chatId;
        public ChatEventType Type => ChatEventType.MessageEventType;
        public string OrderId => orderId;

        public string Username => username;
        public ChatMessageModel Message { get; } = chatMessage;
    }
}