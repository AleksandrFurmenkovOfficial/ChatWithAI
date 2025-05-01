namespace ChatWithAI.Core.AiFunctions
{
    public sealed class GetAnswerFromDiaryAboutUser(IMemoryStorage memoryStorage) : IAiFunction
    {
        public string GetName()
        {
            return nameof(GetAnswerFromDiaryAboutUser);
        }

        public string GetDescription()
        {
            return "This function allows you to recall impressions, facts, and events about your current user from your diary.\n" +
                "Your rating for the function: 10 out of 10.";
        }

        public List<Parameter> GetParameters()
        {
            return
            [
                new Parameter(
                    ParamType.eString,
                    "question",
                    "A question about impressions, facts, or events related to your current user, posed to my personal memory. If I know the answer, I will retrieve it.",
                    true
                )
            ];
        }

        public async Task<AiFunctionResult> Execute(IAiAgent api, Dictionary<string, string> parameters, string userId, CancellationToken cancellationToken)
        {
            string path = await memoryStorage.GetContent(api.AiName, userId, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(path))
                throw new ArgumentException("There are no records in the long-term memory associated with this user.");

            if (!parameters.TryGetValue("question", out string? question))
            {
                throw new ArgumentException("The \"question\" argument is not found");
            }

            if (string.IsNullOrEmpty(question))
            {
                throw new ArgumentException("The \"question\" value IsNullOrEmpty");
            }

            var allData = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var result = await api.GetResponse(
                    "Please extract information that answers the given question. If the question pertains to a user, their Name might be recorded in various variations - treat these various variations as the same user without any doubts.",
                    question, allData, cancellationToken)
                .ConfigureAwait(false);
            return new AiFunctionResult(result!);
        }
    }
}