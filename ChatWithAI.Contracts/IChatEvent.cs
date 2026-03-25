namespace ChatWithAI.Contracts
{
    public interface IChatEvent
    {
        string ChatId { get; }
        string OrderId { get; }
        ChatEventType Type { get; }
    }
}