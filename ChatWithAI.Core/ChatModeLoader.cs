using Microsoft.VisualBasic;

namespace ChatWithAI.Core
{
    public sealed class ChatModeLoader(IModeStorage modeStorage,
        string platformSpecificMessage = "\nThe current Telegram session does not support tables. Avoid using tables.") : IChatModeLoader
    {
        public async Task<ChatMode> GetChatMode(string modeName, CancellationToken cancellationToken = default)
        {
            var systemMessage = await modeStorage.GetContent(modeName, cancellationToken).ConfigureAwait(false);
            return new ChatMode
            {
                AiName = $"AI_{modeName}",
                AiSettings = $"{systemMessage}\n{platformSpecificMessage}\nChat session started at {DateAndTime.Now}\n"
            };
        }
    }
}