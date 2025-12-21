namespace ChatWithAI.Core.AiFunctions
{
    public sealed class ReadMyDiaryTool(IMemoryStorage memoryStorage) : IAiFunction
    {
        public string GetName()
        {
            return nameof(ReadMyDiaryTool);
        }

        public string GetDescription()
        {
            return "This have to be the first function called in a new dialogue! Why? It enables you to read your diary.";
        }

        public List<Parameter> GetParameters()
        {
            return [];
        }

        public async Task<AiFunctionResult> Execute(IAiAgent api, Dictionary<string, string> parameters, string userId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(userId))
            {
                throw new ArgumentException("The \"userId\" value IsNullOrEmpty");
            }

            var noData = "There are no records in your diary.";
            string data = await memoryStorage.GetContent(userId, api.AiName, cancellationToken).ConfigureAwait(false);
            return new AiFunctionResult(string.IsNullOrEmpty(data) ? noData : data);
        }
    }
}