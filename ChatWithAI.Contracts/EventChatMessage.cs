namespace ChatWithAI.Contracts
{
    public class EventChatMessage(string chatId, string orderId, string username, ChatMessage chatMessage) : IChatEvent
    {
        public string ChatId => chatId;
        public ChatEventType Type => ChatEventType.MessageEventType;
        public string OrderId => orderId;

        public string Username => username;
        public ChatMessage Message { get; } = chatMessage;
    }
}