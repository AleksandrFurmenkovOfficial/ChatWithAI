namespace ChatWithAI.Contracts
{
    public class EventChatCtrlCHotkey(string chatId) : IChatEvent
    {
        public string ChatId => chatId;
        public ChatEventType Type => ChatEventType.CtrlCEventType;
        public string OrderId => "";
    }
}