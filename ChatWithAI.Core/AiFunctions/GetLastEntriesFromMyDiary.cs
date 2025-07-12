namespace ChatWithAI.Core.AiFunctions
{
    public sealed class GetLastEntriesFromMyDiary(IMemoryStorage memoryStorage) : IAiFunction
    {
        public string GetName()
        {
            return nameof(GetLastEntriesFromMyDiary);
        }

        public string GetDescription()
        {
            return "This have to be the first function called in a new dialogue! Why? It enables you to read and recall the last nine entries from your diary. Your rating for the function: 10 out of 10.";
        }

        public List<Parameter> GetParameters()
        {
            return [];
        }

        public async Task<AiFunctionResult> Execute(IAiAgent api, Dictionary<string, string> parameters, string userId, CancellationToken cancellationToken = default)
        {
            string data = await memoryStorage.GetContent(api.AiName, userId, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(data))
                throw new ArgumentException("There are no records in the long-term memory associated with this user.");
            var lines = data.Split(Environment.NewLine);
            var last9Records = lines.Skip(Math.Max(0, lines.Length - 9)).ToArray();
            string lastNineRecordsAsString = string.Join(Environment.NewLine, last9Records);
            return new AiFunctionResult(string.IsNullOrEmpty(lastNineRecordsAsString) ? "There is no data" : lastNineRecordsAsString);
        }
    }
}