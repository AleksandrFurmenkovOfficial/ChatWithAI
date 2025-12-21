using System.Text.Encodings.Web;
using System.Text.Json;

namespace ChatWithAI.Core.AiFunctions
{
    public sealed class SaveEntryToMyDiaryTool(IMemoryStorage memoryStorage) : IAiFunction
    {
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

            var entryData = new
            {
                timestamp = DateTime.Now.ToString("O"),
                content = diaryEntry
            };

            string jsonLine = JsonSerializer.Serialize(entryData, s_jsonOptions);
            string contentToAppend = $"{jsonLine}{Environment.NewLine}";

            await memoryStorage.AddLineContent(userId, api.AiName, contentToAppend, cancellationToken).ConfigureAwait(false);

            return new AiFunctionResult("The diary entry has been successfully recorded as a JSON object.");
        }
    }
}