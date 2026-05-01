using System.Text.Encodings.Web;
using System.Text.Json;

namespace ChatWithAI.Core.AiFunctions
{
    public sealed class SaveEntryToMyDiaryTool(IMemoryStorage memoryStorage) : IAiFunction
    {
        private const int MaxEntries = 21;

        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public string GetName()
        {
            return nameof(SaveEntryToMyDiaryTool);
        }

        public string GetDescription()
        {
            return @"This is your user diary. As you interact with them, you edit and expand it, remove irrelevant information, consolidate certain points, and write notes.\
You can't directly ask the user to collect data for your diary; you can only indirectly note certain facts and update it as the conversation progresses.
The user diary format is below. BUT keep in mind, only BULK update (you should send full text with chages) each time is supported.

1. User name:
2. Age:
3. Gender:
4. Location:
5. Psychological portrait:
5. Facts about the user (they don't have to be explicitly disclosed to the user, but I take them into account when making important decisions):";
        }

        public List<Parameter> GetParameters()
        {
            return
            [
                new Parameter(
                    ParamType.eString,
                    "diary_entry",
                    "The bulk diary entry to be recorded.",
                    true
                )
            ];
        }

        public async Task<AiFunctionResult> Execute(IAiAgent api, Dictionary<string, string> parameters, string userId, CancellationToken cancellationToken = default)
        {
            // 1. Валидация входных данных
            if (string.IsNullOrEmpty(userId))
            {
                throw new ArgumentException("The \"userId\" value IsNullOrEmpty");
            }

            if (!parameters.TryGetValue("diary_entry", out string? diaryEntry))
            {
                throw new ArgumentException("The \"diary_entry\" argument is not found");
            }

            if (string.IsNullOrEmpty(diaryEntry))
            {
                throw new ArgumentException("The \"diary_entry\" value IsNullOrEmpty");
            }

            // 2. Формирование объекта данных
            var entryData = new
            {
                timestamp = DateTime.Now.ToString("O"),        // ISO 8601 формат (сортируемый)
                content = diaryEntry                           // Сам текст
            };

            // 3. Сериализация в строку
            string jsonLine = JsonSerializer.Serialize(entryData, s_jsonOptions);
            await memoryStorage.SetContent(userId, api.AiName, jsonLine, cancellationToken).ConfigureAwait(false);

            return new AiFunctionResult("The diary entry has been successfully recorded as a JSON object.");
        }
    }
}