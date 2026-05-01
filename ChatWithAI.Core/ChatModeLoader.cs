using Microsoft.VisualBasic;

namespace ChatWithAI.Core
{
    public sealed class ChatModeLoader(IModeStorage modeStorage) : IChatModeLoader // \nThe current Telegram session does not support tables. Avoid using tables.
    {
        public async Task<ChatMode> GetChatMode(string modeName, CancellationToken cancellationToken = default)
        {
            var systemMessage = await modeStorage.GetContent(modeName, cancellationToken).ConfigureAwait(false);
            return new ChatMode
            {
                AiName = $"AI_{modeName}",
                AiSettings = $"{systemMessage}\nChat session started at {DateAndTime.Now}\n", // \n{platformSpecificMessage}
                UseFunctions = modeName == "common" || modeName == "base",
                UseImage = modeName == "photoeditor" || modeName == "docs",
                UseFlash = modeName == "docs"
            };
        }
    }
}