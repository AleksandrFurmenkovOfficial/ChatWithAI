namespace ChatWithAI.Core.ChatMessageActions
{
    public sealed class CancelAction : IChatMessageAction
    {
        public static ActionId Id => new("Cancel");

        public ActionId GetId => Id;

        public Task Run(IChat chat, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}