using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Mime;

namespace ChatWithAI.Providers.Google
{
    internal sealed partial class GoogleGeminiAgent : HttpBasedAgent
    {
        private readonly Uri m_endpoint;
        private readonly int m_max_tokens;
        private readonly string m_model;
        private readonly double m_temperature;

        public GoogleGeminiAgent(
            string aiName,
            string systemMessage,
            bool enableFunctions,
            GoogleGeminiConfig config,
            IAiImagePainter? aiImagePainter,
            IAiFunctionsManager aiFunctionsManager)
            : base(aiName, systemMessage, enableFunctions, aiImagePainter, aiFunctionsManager)
        {
            m_endpoint = new Uri(config.ApiEndpoint);
            m_model = config.Model;
            m_max_tokens = config.MaxTokens;
            m_temperature = config.Temperature;

            m_httpClient.DefaultRequestHeaders.Add("x-goog-api-key", config.ApiKey);
            m_httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        }

        protected override string GetSingleshotUrl() => $"{m_endpoint}/models/{m_model}:generateContent";

        protected override string GetStreamUrl() => $"{m_endpoint}/models/{m_model}:streamGenerateContent?alt=sse";

        protected override List<ChatMessage> GetProcessedMessages(IEnumerable<ChatMessage> messages)
        {
            var newMessages = new List<ChatMessage>();
            foreach (var message in messages)
            {
                var imagePart = message.Content
                    .OfType<ImageContentItem>();
                if (imagePart != null && imagePart.Any())
                {
                    var msg = message.Clone();
                    msg.Content.Clear();

                    foreach (var item in imagePart)
                    {
                        msg.Content.AddRange([item]);
                        newMessages.Add(msg);
                    }
                }

                var audioPart = message.Content.OfType<AudioContentItem>();
                if (audioPart != null && audioPart.Any())
                {
                    var msg = message.Clone();
                    msg.Content.Clear();

                    foreach (var item in audioPart)
                    {
                        msg.Content.AddRange([item]);
                        newMessages.Add(msg);
                    }
                }

                var textParts = message.Content
                    .OfType<TextContentItem>()
                    .Where(t => !string.IsNullOrEmpty(t.Text))
                    .Select(t => t.Text!.Trim())
                    .ToList();

                if (textParts.Count > 0)
                {
                    var msg = message.Clone();
                    msg.Content.Clear();
                    msg.Content.AddRange(ChatMessage.GetTextContentItem(message));
                    newMessages.Add(msg);
                }

                var jsonPart = message.Content
                    .OfType<JsonObjectContentItem>();
                if (jsonPart != null && jsonPart.Any())
                {
                    var msg = message.Clone();
                    msg.Content.Clear();

                    foreach (var item in jsonPart)
                    {
                        msg.Content.AddRange([item]);
                        newMessages.Add(msg);
                    }
                }
            }

            return base.GetProcessedMessages(newMessages);
        }

        protected override string GetJsonPayload(
            IEnumerable<ChatMessage> messages,
            string systemMessage,
            bool useStream = false,
            bool useTools = false,
            bool useCache = false)
        {
            List<object> requestMessages = GetGeminiMessages(GetProcessedMessages(messages));

            var requestBody = new Dictionary<string, object?>
            {
                ["systemInstruction"] = new { parts = new[] { new { text = systemMessage } } },
                ["contents"] = requestMessages,
                ["generationConfig"] = new
                {
                    temperature = m_temperature,
                    maxOutputTokens = m_max_tokens,
                },
                ["tools"] = useTools && m_aiFunctionsManager != null ? new[] { new { functionDeclarations = JArray.Parse(m_aiFunctionsManager.Representation()) } } : null,
                ["toolConfig"] = useTools && m_aiFunctionsManager != null ? new { functionCallingConfig = new { mode = "AUTO" } } : null
            };

            requestBody = requestBody.Where(kvp => kvp.Value != null)
                         .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            return JsonConvert.SerializeObject(requestBody, GetJsonSerializerSettings());
        }

        protected override bool SkipLine(string line)
        {
            return !line.StartsWith("data:", StringComparison.InvariantCultureIgnoreCase) || line["data:".Length..] == "{\"type\": \"ping\"}";
        }

        protected override string GetTextFromResponse(string responseContent)
        {
            var response = JsonConvert.DeserializeObject<GenerateContentResponse>(responseContent, GetJsonSerializerSettings());
            return response?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
        }

        protected override ChatMessage CreateCallMessage(string inputJson, string toolUseId, string functionName)
        {
            return new ChatMessage
            {
                Role = MessageRole.eRoleAI,
                Content =
                [
                    ChatMessage.CreateJsonObject(new
                    {
                        functionCall = new
                        {
                            name = functionName,
                            args = string.IsNullOrEmpty(inputJson)
                                ? new Dictionary<string, string>()
                                : (object)JObject.Parse(inputJson)
                        }
                    })
                ]
            };
        }

        protected override ChatMessage CreateResultMessage(string toolUseId, AiFunctionResult result, bool isError)
        {
            var message = new ChatMessage
            {
                Role = MessageRole.eRoleFunction,
                Content =
                [
                    ChatMessage.CreateJsonObject(new
                    {
                        functionResponse = new
                        {
                            name = toolUseId,
                            response = new
                            {
                                result = result.Content.OfType<TextContentItem>().FirstOrDefault()?.Text,
                                imageUrl = result.Content.OfType<ImageContentItem>().FirstOrDefault()?.ImageUrl
                            }
                        }
                    })
                ]
            };

            var imageItem = result.Content.OfType<ImageContentItem>().FirstOrDefault();
            if (imageItem != null) message.Content.Add(imageItem);

            return message;
        }

        private static List<object> GetGeminiMessages(IEnumerable<ChatMessage> messages) // TODO:
        {
            var contentsList = new List<object>();
            foreach (var message in messages)
            {
                var textParts = message.Content
                    .OfType<TextContentItem>()
                    .Where(t => !string.IsNullOrEmpty(t.Text))
                    .Select(t => t.Text!.Trim())
                    .ToList();

                var imagePart = message.Content
                    .OfType<ImageContentItem>()
                    .FirstOrDefault();

                var audioPart = message.Content
                    .OfType<AudioContentItem>()
                    .FirstOrDefault();

                var jsonPart = message.Content
                    .OfType<JsonObjectContentItem>()
                    .FirstOrDefault();

                if (textParts.Count == 0 && imagePart == null && jsonPart == null && audioPart == null)
                    continue;

                string role = message.Role switch
                {
                    MessageRole.eRoleUser => "user",
                    MessageRole.eRoleFunction => "user",
                    _ => "model",
                };

                contentsList.Add(new
                {
                    role,
                    parts = CreateMessageParts(textParts, imagePart, audioPart, jsonPart)
                });
            }

            return contentsList;
        }

        private static object[] CreateMessageParts(
            List<string> textParts,
            ImageContentItem? imagePart,
            AudioContentItem? audioPart,
            JsonObjectContentItem? jsonItem)
        {
            if (jsonItem != null)
            {
                return [jsonItem.JsonObject!];
            }

            if (imagePart != null)
            {
                if (textParts != null && textParts.Count > 0)
                {
                    return
                    [
                        new { text = string.Join("\n", textParts) },
                        new
                        {
                            inlineData = new
                            {
                                mimeType = MediaTypeNames.Image.Webp,
                                data = imagePart.ImageInBase64
                            }
                        }
                    ];
                }
                else
                {
                    return
                    [
                        new
                        {
                            inlineData = new
                            {
                                mimeType = MediaTypeNames.Image.Webp,
                                data = imagePart.ImageInBase64
                            }
                        }
                    ];
                }
            }

            if (audioPart != null)
            {
                return
                [
                    new
                    {
                        inlineData = new
                        {
                            mimeType = "audio/mp3",
                            data = audioPart.AudioInBase64
                        }
                    }
                ];
            }

            return [new { text = textParts != null ? string.Join("\n", textParts) : "" }];
        }

        protected override bool ProcessStreamChunck(
            Func<ResponseStreamChunk, Task<bool>> responseStreamChunkGetter,
            ref string functionName,
            ref Dictionary<string, string> functionCalls,
            ref string toolUseId,
            string chatId,
            string chunk,
            bool maxDeepAchived)
        {
            if (string.IsNullOrEmpty(chunk)) return false;

            var data = chunk["data:".Length..].Trim();
            if (SkipLine(chunk)) return false;

            GenerateContentResponse? streamResponse;
            try
            {
                streamResponse = JsonConvert.DeserializeObject<GenerateContentResponse>(data, GetJsonSerializerSettings());
            }
            catch (JsonSerializationException ex)
            {
                throw new InvalidOperationException($"JSON Deserialization error in ProcessStreamChunck: {ex.Message} - Chunk Data: {data}");
            }

            if (streamResponse == null || streamResponse.Candidates == null)
                return false;

            bool isDone = false;
            foreach (var candidate in streamResponse.Candidates)
            {
                if (candidate?.Content?.Parts == null)
                    continue;

                foreach (var part in candidate.Content.Parts)
                {
                    if (part.FunctionCall != null)
                    {
                        var parameters = JsonConvert.SerializeObject(part.FunctionCall.Args, GetJsonSerializerSettings());

                        isDone = ProcessFunctionCall(
                            part.FunctionCall.Name,
                            parameters,
                            part.FunctionCall.Name,
                            chatId,
                            responseStreamChunkGetter,
                            maxDeepAchived).ConfigureAwait(false).GetAwaiter().GetResult();

                        if (isDone)
                            return true;
                    }
                    else if (!string.IsNullOrEmpty(part.Text))
                    {
                        isDone = responseStreamChunkGetter(new ResponseStreamChunk(part.Text)).ConfigureAwait(false).GetAwaiter().GetResult();
                        if (isDone)
                            return true;
                    }
                }
            }

            return isDone;
        }
    }
}