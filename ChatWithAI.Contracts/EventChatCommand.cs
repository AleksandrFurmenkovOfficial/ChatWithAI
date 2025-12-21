using ChatWithAI.Contracts.Model;

namespace ChatWithAI.Contracts
{
    public class EventChatCommand(string chatId, string orderId, string username, IChatCommand command, ChatMessageModel message, string commandData) : IChatEvent
    {
        public string ChatId => chatId;
        public ChatEventType Type => ChatEventType.CommandEventType;
        public string OrderId => orderId;

        public string Username => username;

        public ChatMessageModel Message { get; } = message;
        public IChatCommand Command { get; } = command;
        public string CommandData { get; } = commandData;
    }
}