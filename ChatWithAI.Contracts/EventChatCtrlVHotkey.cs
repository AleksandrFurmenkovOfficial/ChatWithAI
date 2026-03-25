namespace ChatWithAI.Contracts
{
    public class EventChatCtrlVHotkey(string chatId) : IChatEvent
    {
        public string ChatId => chatId;
        public ChatEventType Type => ChatEventType.CtrlVEventType;
        public string OrderId => "";
    }
}