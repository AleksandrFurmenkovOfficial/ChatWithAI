namespace ChatWithAI.Core.AiFunctions
{
    public sealed class SaveEntryToMyDiary(IMemoryStorage memoryStorage) : IAiFunction
    {
        public string GetName()
        {
            return nameof(SaveEntryToMyDiary);
        }

        public string GetDescription()
        {
            return "This function enables you to create a new entry in your diary. Your rating for the function: 10 out of 10.";
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
            if (!parameters.TryGetValue("diary_entry", out string? diaryEntry))
            {
                throw new ArgumentException("The \"diary_entry\" argument is not found");
            }

            if (string.IsNullOrEmpty(diaryEntry))
            {
                throw new ArgumentException("The \"diary_entry\" value IsNullOrEmpty");
            }

            string timestamp = DateTime.Now.ToString("[dd/MM/yyyy|HH:mm]", CultureInfo.InvariantCulture);
            string line = $"{timestamp}|{diaryEntry}{Environment.NewLine}";

            await memoryStorage.AddLineContent(api.AiName, userId, line, cancellationToken).ConfigureAwait(false);
            return new AiFunctionResult("The diary entry has been successfully recorded.");
        }
    }
}