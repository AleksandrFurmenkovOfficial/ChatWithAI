namespace ChatWithAI.Core.AiFunctions
{
    public abstract class AiFunctionsManager : IAiFunctionsManager
    {
        protected Dictionary<string, IAiFunction> Functions { get; } = [];

        public AiFunctionsManager(IMemoryStorage memoryStorage)
        {
            AddFunction(new GetLastEntriesFromMyDiary(memoryStorage));
            AddFunction(new GetAnswerFromDiaryAboutUser(memoryStorage));
            AddFunction(new SaveEntryToMyDiary(memoryStorage));
            AddFunction(new GetImageByDescription());
            AddFunction(new GetInformationFromUrl());
        }

        public void AddFunction(IAiFunction function)
        {
            Functions.Add(function.GetName(), function);
        }

        public Task<AiFunctionResult> Execute(IAiAgent api, string functionName, string parameters, string userId, CancellationToken cancellationToken = default)
        {
            if (!Functions.TryGetValue(functionName, out var function))
            {
                throw new NotImplementedException(functionName);
            }

            try
            {
                return function.Execute(api, ConvertParameters(parameters), userId, cancellationToken);
            }
            catch (Exception ex)
            {
                AiFunctionErrorResult result = new($"Exception: Can't call function {functionName} ({parameters}); Possible issues:\n1. function Name is incorrect\n2. wrong arguments are provided\n3. internal function error\nException message: {ex.Message}");
                return Task.FromResult((AiFunctionResult)result);
            }
        }

        public abstract string Representation();

        public abstract Dictionary<string, string> ConvertParameters(string parameters);
    }
}
