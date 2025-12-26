using ChatWithAI.Contracts.Model;
using System.Collections.Concurrent;

namespace ChatWithAI.Core.ChatCommands
{
    public sealed class AppAddAccess(ConcurrentDictionary<string, AppVisitor> visitors, IMessenger messenger) : IChatCommand
    {
        string IChatCommand.Name => "add";
        bool IChatCommand.IsAdminOnlyCommand => true;

        public Task Execute(IChat chat, ChatMessageModel message, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            var id = message.GetTextContentItems()[0].Text!.Trim();
            if (string.IsNullOrEmpty(id))
                return Task.FromCanceled(cancellationToken);

            _ = visitors.AddOrUpdate(id, _ =>
            {
                var arg = new AppVisitor(true, Strings.Unknown, DateTime.UtcNow);
                return arg;
            }, (_, arg) =>
            {
                arg.Access = true;
                return arg;
            });

            var showVisitorsCommand = new AppShowVisitors(visitors, messenger);
            return showVisitorsCommand.Execute(chat, message, cancellationToken);
        }
    }
}
