namespace ChatWithAI.Providers.Google
{
    internal sealed class GeminiGenerateContentRequest
    {
        public List<GeminiContent>? Contents { get; set; }
        public List<GeminiTool>? Tools { get; set; }
        public GeminiToolConfig? ToolConfig { get; set; }
        public GeminiGenerationConfig? GenerationConfig { get; set; }
        public GeminiContent? SystemInstruction { get; set; }
    }

    internal sealed class GeminiContent
    {
        public string? Role { get; set; }
        public List<GeminiPart>? Parts { get; set; }
    }

    internal sealed class GeminiPart
    {
        public string? Text { get; set; }
        public GeminiInlineData? InlineData { get; set; }
        public GeminiFunctionCall? FunctionCall { get; set; }
        public GeminiFunctionResponse? FunctionResponse { get; set; }
        public bool? Thought { get; set; }
        public string? ThoughtSignature { get; set; }
    }

    internal sealed class GeminiInlineData
    {
        public string? MimeType { get; set; }
        public string? Data { get; set; }
    }

    internal sealed class GeminiFunctionCall
    {
        public string? Name { get; set; }
        public Dictionary<string, object>? Args { get; set; }
    }

    internal sealed class GeminiFunctionResponse
    {
        public string? Name { get; set; }
        public Dictionary<string, object>? Response { get; set; }
    }

    internal sealed class GeminiTool
    {
        public List<GeminiFunctionDeclaration>? FunctionDeclarations { get; set; }
        public object? GoogleSearch { get; set; }
    }

    internal sealed class GeminiFunctionDeclaration
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public GeminiFunctionParameters? Parameters { get; set; }
    }

    internal sealed class GeminiFunctionParameters
    {
        public string? Type { get; set; }
        public Dictionary<string, GeminiFunctionProperty>? Properties { get; set; }
        public List<string>? Required { get; set; }
    }

    internal sealed class GeminiFunctionProperty
    {
        public string? Type { get; set; }
        public string? Description { get; set; }
    }

    internal sealed class GeminiToolConfig
    {
        public GeminiFunctionCallingConfig? FunctionCallingConfig { get; set; }
    }

    internal sealed class GeminiFunctionCallingConfig
    {
        public string? Mode { get; set; }
    }

    internal sealed class GeminiGenerationConfig
    {
        public double? Temperature { get; set; }
        public double? TopP { get; set; }
        public int? TopK { get; set; }
        public int? MaxOutputTokens { get; set; }
        public GeminiThinkingConfig? ThinkingConfig { get; set; }
    }

    internal sealed class GeminiThinkingConfig
    {
        public bool IncludeThoughts { get; set; } = true;
        public string? ThinkingLevel { get; set; }
    }

    internal sealed class GeminiGenerateContentResponse
    {
        public List<GeminiCandidate>? Candidates { get; set; }
        public GeminiUsageMetadata? UsageMetadata { get; set; }
    }

    internal sealed class GeminiCandidate
    {
        public GeminiContent? Content { get; set; }
        public string? FinishReason { get; set; }
        public GeminiGroundingMetadata? GroundingMetadata { get; set; }
    }

    internal sealed class GeminiGroundingMetadata
    {
        public List<GeminiGroundingChunk>? GroundingChunks { get; set; }
        public GeminiSearchEntryPoint? SearchEntryPoint { get; set; }
    }

    internal sealed class GeminiGroundingChunk
    {
        public GeminiWebSource? Web { get; set; }
    }

    internal sealed class GeminiWebSource
    {
        public string? Uri { get; set; }
        public string? Title { get; set; }
    }

    internal sealed class GeminiSearchEntryPoint
    {
        public string? RenderedContent { get; set; }
    }

    internal sealed class GeminiUsageMetadata
    {
        public int? PromptTokenCount { get; set; }
        public int? CandidatesTokenCount { get; set; }
        public int? TotalTokenCount { get; set; }
    }
}
