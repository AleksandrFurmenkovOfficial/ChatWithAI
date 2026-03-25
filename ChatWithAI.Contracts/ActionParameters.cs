namespace ChatWithAI.Contracts
{
    public readonly struct ActionParameters(ActionId actionId, string messageId)
    {
        public readonly ActionId ActionId { get; } = actionId;
        public readonly string MessageId { get; } = messageId;
    }
}