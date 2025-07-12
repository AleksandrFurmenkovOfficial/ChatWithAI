namespace ChatWithAI.Core.ChatMessageActions
{
    public sealed class ContinueAction : IChatMessageAction
    {
        public static ActionId Id => new("Continue");

        public ActionId GetId => Id;

        public Task Run(IChat chat, CancellationToken cancellationToken = default)
        {
            return chat.ContinueLastResponse(cancellationToken);
        }
    }
}