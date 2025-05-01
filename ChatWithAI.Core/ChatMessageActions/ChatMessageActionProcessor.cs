

namespace ChatWithAI.Core.ChatMessageActions
{
    public sealed class ChatMessageActionProcessor : IChatMessageActionProcessor
    {
        private readonly Dictionary<ActionId, IChatMessageAction> actions = [];

        public ChatMessageActionProcessor(IEnumerable<IChatMessageAction> actions)
        {
            foreach (var action in actions)
            {
                this.actions.Add(new ActionId($"{action.GetId.Name}"), action);
            }
        }

        public async Task HandleMessageAction(IChat chat, ActionParameters actionCallParameters,
            CancellationToken cancellationToken)
        {
            if (!actions.TryGetValue(actionCallParameters.ActionId, out var action)) return;

            await action.Run(chat, cancellationToken).ConfigureAwait(false);
        }
    }
}