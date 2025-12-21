namespace ChatWithAI.Core.ChatMessageActions
{
    public sealed class RetryAction : RegenerateAction
    {
        public static new ActionId Id => new("Retry");

        public override ActionId GetId => Id;

        public override async Task Run(IChat chat, CancellationToken cancellationToken = default)
        {
            await base.Run(chat, cancellationToken).ConfigureAwait(false);
        }
    }
}