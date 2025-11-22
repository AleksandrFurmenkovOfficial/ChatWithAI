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
            return "This function enables you to create a new entry (record) in your personal diary. It saves data as structured JSON.";
        }

        public List<Parameter> GetParameters()
        {
            return
            [
                new Parameter(
                    ParamType.eString,
                    "diary_entry",
                    "The diary entry to be recorded, encompassing your plans, facts, thoughts, reasoning, conjectures, and impressions.",
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

            // 4. Чтение существующих записей и применение LIFO-вытеснения
            string existingContent = await memoryStorage.GetContent(userId, api.AiName, cancellationToken).ConfigureAwait(false);
            var entries = existingContent
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            // 5. Если записей >= MaxEntries, удаляем самую старую (первую)
            while (entries.Count >= MaxEntries)
            {
                entries.RemoveAt(0);
            }

            // 6. Добавляем новую запись
            entries.Add(jsonLine);

            // 7. Сохранение всех записей
            string newContent = string.Join(Environment.NewLine, entries) + Environment.NewLine;
            await memoryStorage.SetContent(userId, api.AiName, newContent, cancellationToken).ConfigureAwait(false);

            return new AiFunctionResult("The diary entry has been successfully recorded as a JSON object.");
        }
    }
}