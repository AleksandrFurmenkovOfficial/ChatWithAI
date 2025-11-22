using Microsoft.VisualBasic;

namespace ChatWithAI.Core
{
    public sealed class ChatModeLoader(IModeStorage modeStorage,
        string platformSpecificMessage = "\nТелеграм в котором сейчас проходит сеанс связи не поддерживает таблицы. Использования таблиц следует избегать.") : IChatModeLoader
    {
        public async Task<ChatMode> GetChatMode(string modeName, CancellationToken cancellationToken = default)
        {
            var systemMessage = await modeStorage.GetContent(modeName, cancellationToken).ConfigureAwait(false);
            return new ChatMode
            {
                AiName = $"Vivy_{modeName}",
                AiSettings = systemMessage + "\n" + platformSpecificMessage + "\n" + $"Сеанс чата начат в {DateAndTime.Now}\n"
            };
        }
    }
}