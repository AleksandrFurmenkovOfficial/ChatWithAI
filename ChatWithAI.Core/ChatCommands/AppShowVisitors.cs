using ChatWithAI.Contracts.Model;
using System.Collections.Concurrent;

namespace ChatWithAI.Core.ChatCommands
{
    public sealed class AppShowVisitors(ConcurrentDictionary<string, AppVisitor> visitors, IMessenger messenger) : IChatCommand
    {
        string IChatCommand.Name => "vis";
        bool IChatCommand.IsAdminOnlyCommand => true;

        public Task Execute(IChat chat, ChatMessageModel message, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            string vis = visitors.Aggregate("Visitors:\n",
                (current, item) => current + $"{item.Key} - {item.Value.Name}:{item.Value.Access} - {item.Value.LatestAccess}\n");
            return messenger.SendTextMessage(chat.Id, new MessengerMessageDTO { TextContent = vis });
        }
    }
}