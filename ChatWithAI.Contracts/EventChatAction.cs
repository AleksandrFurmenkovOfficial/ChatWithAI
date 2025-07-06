namespace ChatWithAI.Contracts
{
    public class EventChatAction(string chatId, string orderId, ActionParameters actionParameters) : IChatEvent
    {
        public string ChatId => chatId;
        public ChatEventType Type => ChatEventType.ActionEventType;
        public string OrderId => orderId;

        public ActionParameters ActionParameters => actionParameters;
    }
}