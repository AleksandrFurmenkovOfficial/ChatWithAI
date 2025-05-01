using Newtonsoft.Json;

namespace ChatWithAI.Providers.Google
{
    internal sealed partial class GoogleGeminiAgent : IAiAgent, IDisposable
    {
        private sealed class GenerateContentResponse
        {
            [JsonProperty("candidates")]
            public List<Candidate>? Candidates { get; set; }
        }

        private sealed class Candidate
        {
            [JsonProperty("content")]
            public Content? Content { get; set; }

            [JsonProperty("finishReason")]
            public string? FinishReason { get; set; }
        }

        private sealed class Content
        {
            [JsonProperty("parts")]
            public List<Part>? Parts { get; set; }

            [JsonProperty("role")]
            public string? Role { get; set; }
        }

        private sealed class Part
        {
            [JsonProperty("text")]
            public string? Text { get; set; }

            [JsonProperty("functionCall")]
            public FunctionCall? FunctionCall { get; set; }

            [JsonProperty("functionResponse")]
            public FunctionResponse? FunctionResponse { get; set; }
        }

        private sealed class FunctionCall
        {
            [JsonProperty("name")]
            public string Name { get; set; } = "";

            [JsonProperty("args")]
            public Dictionary<string, object> Args { get; set; } = new();
        }

        private sealed class FunctionResponse
        {
            [JsonProperty("name")]
            public string Name { get; set; } = "";

            [JsonProperty("response")]
            public object Response { get; set; } = new();
        }
    }
}