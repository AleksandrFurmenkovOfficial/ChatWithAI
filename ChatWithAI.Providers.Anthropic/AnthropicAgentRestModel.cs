using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace ChatWithAI.Providers.Anthropic
{
    internal sealed partial class AnthropicAgent : IAiAgent, IDisposable
    {
        private sealed class EventType
        {
            [JsonPropertyName("type")]
            public string? Type { get; set; }
        }

        private sealed class ContentBlockStart
        {
            [JsonPropertyName("content_block")]
            public ToolUseContentBlock? ContentBlock { get; set; }
        }

        private sealed class ToolUseContentBlock
        {
            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("input")]
            public object? Input { get; set; }
        }

        private sealed class ContentBlockDelta
        {
            [JsonPropertyName("delta")]
            public Delta? Delta { get; set; }
        }

        private sealed class Delta
        {
            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("text")]
            public string? Text { get; set; }

            [JsonPropertyName("partial_json")]
            public string? PartialJson { get; set; }
        }

        public sealed class RootObject
        {
            [JsonProperty("id")]
            public string? Id { get; set; }

            [JsonProperty("type")]
            public string? Type { get; set; }

            [JsonProperty("role")]
            public string? Role { get; set; }

            [JsonProperty("model")]
            public string? Model { get; set; }

            [JsonProperty("content")]
            public List<Content>? Content { get; set; }

            [JsonProperty("stop_reason")]
            public string? StopReason { get; set; }

            [JsonProperty("stop_sequence")]
            public object? StopSequence { get; set; }

            [JsonProperty("usage")]
            public Usage? Usage { get; set; }
        }

        public sealed class Content
        {
            [JsonProperty("type")]
            public string? Type { get; set; }

            [JsonProperty("text")]
            public string? Text { get; set; }
        }

        public sealed class Usage
        {
            [JsonProperty("input_tokens")]
            public int InputTokens { get; set; }

            [JsonProperty("output_tokens")]
            public int OutputTokens { get; set; }
        }
    }
}