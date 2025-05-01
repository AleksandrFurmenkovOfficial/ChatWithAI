using System.Collections.Concurrent;


namespace ChatWithAI.Core.ChatCommands
{
    public sealed class DelAccess(ConcurrentDictionary<string, IAppVisitor> visitors) : IChatCommand
    {
        string IChatCommand.Name => "del";
        bool IChatCommand.IsAdminOnlyCommand => true;

        public Task Execute(IChat chat, ChatMessage message, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            var id = ChatMessage.GetTextContentItem(message)[0].Text!.Trim();
            _ = visitors.AddOrUpdate(id, _ =>
            {
                var arg = new AppVisitor(false, Strings.Unknown, DateTime.UtcNow);
                return arg;
            }, (_, arg) =>
            {
                arg.Access = false;
                return arg;
            });

            var showVisitorsCommand = new ShowVisitors(visitors);
            return showVisitorsCommand.Execute(chat, message, cancellationToken);
        }
    }
}
