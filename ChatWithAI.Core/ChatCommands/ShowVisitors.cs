using System.Collections.Concurrent;

namespace ChatWithAI.Core.ChatCommands
{
    public sealed class ShowVisitors(ConcurrentDictionary<string, IAppVisitor> visitors) : IChatCommand
    {
        string IChatCommand.Name => "vis";
        bool IChatCommand.IsAdminOnlyCommand => true;

        public Task Execute(IChat chat, ChatMessage message, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            string vis = visitors.Aggregate("Visitors:\n",
                (current, item) => current + $"{item.Key} - {item.Value.Name}:{item.Value.Access} - {item.Value.LatestAccess}\n");
            return chat.SendSystemMessage(vis, cancellationToken);
        }
    }
}