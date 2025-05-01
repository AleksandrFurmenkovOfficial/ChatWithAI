namespace ChatWithAI.Core.ChatCommands
{
    public sealed class ChatCommandProcessor : IChatCommandProcessor
    {
        private readonly IAdminChecker adminChecker;
        private readonly Dictionary<string, IChatCommand> commands = [];

        public ChatCommandProcessor(
            IEnumerable<IChatCommand> commands,
            IAdminChecker adminChecker)
        {
            this.adminChecker = adminChecker;
            foreach (var command in commands)
            {
                this.commands.Add($"/{command.Name}", command);
            }
        }

        public async Task<bool> ExecuteIfChatCommand(IChat chat, ChatMessage message,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return await Task.FromCanceled<bool>(cancellationToken).ConfigureAwait(false);
            }

            if (message.Content == null || message.Content.Count == 0)
            {
                return false;
            }

            var textItem = ChatMessage.GetTextContentItem(message).FirstOrDefault();
            if (string.IsNullOrEmpty(textItem?.Text))
                return false;

            var text = textItem.Text;
            foreach ((string commandName, IChatCommand command) in commands.Where(value =>
                         text.Trim().Contains(value.Key, StringComparison.InvariantCultureIgnoreCase)))
            {
                if (command.IsAdminOnlyCommand && !adminChecker.IsAdmin(chat.Id))
                {
                    return false;
                }

                textItem.Text = text[commandName.Length..];
                await command.Execute(chat, message, cancellationToken).ConfigureAwait(false);
                return true;
            }

            return false;
        }
    }
}