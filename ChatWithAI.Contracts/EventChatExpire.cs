namespace ChatWithAI.Contracts
{
    public class EventChatExpire(string chatId) : IChatEvent
    {
        public string ChatId => chatId;
        public ChatEventType Type => ChatEventType.ExpireEventType;
        public string OrderId => "";
    }
}