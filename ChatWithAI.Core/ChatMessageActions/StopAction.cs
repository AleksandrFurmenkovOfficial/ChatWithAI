

namespace ChatWithAI.Core.ChatMessageActions
{
    public sealed class StopAction : IChatMessageAction
    {
        public static ActionId Id => new("Stop");

        public ActionId GetId => Id;

        public Task Run(IChat chat, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}