using ChatWithAI.Contracts.Model;
using System.Collections.Concurrent;

namespace ChatWithAI.Core.ChatCommands
{
    public sealed class AppDelAccess(ConcurrentDictionary<string, AppVisitor> visitors, IMessenger messenger) : IChatCommand
    {
        string IChatCommand.Name => "del";
        bool IChatCommand.IsAdminOnlyCommand => true;

        public Task Execute(IChat chat, ChatMessageModel message, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            var id = message.GetTextContentItems()[0].Text!.Trim();
            _ = visitors.AddOrUpdate(id, _ =>
            {
                var arg = new AppVisitor(false, Strings.Unknown, DateTime.UtcNow);
                return arg;
            }, (_, arg) =>
            {
                arg.Access = false;
                return arg;
            });

            var showVisitorsCommand = new AppShowVisitors(visitors, messenger);
            return showVisitorsCommand.Execute(chat, message, cancellationToken);
        }
    }
}
