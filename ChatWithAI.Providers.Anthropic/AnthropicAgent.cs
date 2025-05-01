using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Mime;

namespace ChatWithAI.Providers.Anthropic
{
    internal sealed partial class AnthropicAgent : HttpBasedAgent
    {
        private readonly Uri m_endpoint;
        private readonly long m_max_tokens;
        private readonly string m_model;
        private readonly double m_temperature;

        public AnthropicAgent(
            string aiName,
            string systemMessage,
            bool enableFunctions,
            AnthropicConfig config,
            IAiImagePainter? aiImagePainter,
            IAiFunctionsManager aiFunctionsManager)
            : base(aiName, systemMessage, enableFunctions, aiImagePainter, aiFunctionsManager)
        {
            m_endpoint = new Uri(config.ApiEndpoint);
            m_model = config.Model;
            m_max_tokens = config.MaxTokens;
            m_temperature = config.Temperature;

            m_httpClient.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
            m_httpClient.DefaultRequestHeaders.Add("anthropic-version", config.ApiVersion);
            m_httpClient.DefaultRequestHeaders.Add("anthropic-beta", "token-efficient-tools-2025-02-19");
        }

        protected override string GetSingleshotUrl() => m_endpoint.ToString();

        protected override string GetStreamUrl() => m_endpoint.ToString();

        protected override string GetJsonPayload(
            IEnumerable<ChatMessage> messages,
            string systemMessage,
            bool useStream = false,
            bool useTools = false,
            bool useCache = true)
        {
            List<object> requestMessages = GetAnthropicMessages(GetProcessedMessages(messages));

            var systemMessageObject = new Dictionary<string, object?>
            {
                ["type"] = "text",
                ["text"] = systemMessage,
                ["cache_control"] = new { type = "ephemeral" }
            };

            systemMessageObject = systemMessageObject.Where(kvp => kvp.Value != null)
                         .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            var requestBody = new Dictionary<string, object?>
            {
                ["model"] = m_model,
                ["system"] = new[] { systemMessageObject },
                ["max_tokens"] = m_max_tokens,
                ["temperature"] = m_temperature,
                ["stream"] = useStream,
                ["tools"] = (useTools && m_aiFunctionsManager != null) ? JArray.Parse(m_aiFunctionsManager.Representation()) : null,
                ["messages"] = requestMessages
            };

            requestBody = requestBody.Where(kvp => kvp.Value != null)
                         .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            return JsonConvert.SerializeObject(requestBody, GetJsonSerializerSettings());
        }

        protected override string GetTextFromResponse(string responseContent)
        {
            var deserializedObject = JsonConvert.DeserializeObject<RootObject>(responseContent, GetJsonSerializerSettings());
            if (deserializedObject?.Content != null)
            {
                foreach (var content in deserializedObject.Content)
                {
                    if (content.Type == "text")
                    {
                        return content.Text!;
                    }
                }
            }

            return responseContent;
        }

        protected override bool SkipLine(string line)
        {
            const string dataPingTag = "data: {\"type\": \"ping\"}";
            const string dataTag = "data:";
            const string eventTag = "event:";

            return string.IsNullOrEmpty(line)
                || line == dataPingTag
                || line.StartsWith(eventTag, StringComparison.InvariantCultureIgnoreCase)
                || !line.StartsWith(dataTag, StringComparison.InvariantCultureIgnoreCase);
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

            var contentData = chunk["data:".Length..].Trim();
            if (SkipLine(chunk)) return false;

            string eventType = GetEventType(contentData);
            if (string.IsNullOrWhiteSpace(eventType))
            {
                throw new InvalidOperationException($"JSON Deserialization error in ProcessStreamChunck, Chunk Data: {contentData}");
            }

            bool isDone = false;
            switch (eventType)
            {
                case "content_block_start":
                    ProcessContentBlockStart(contentData, ref toolUseId, ref functionName);
                    break;

                case "content_block_delta":
                    isDone = ProcessContentBlockDelta(responseStreamChunkGetter, functionName, functionCalls, contentData, CancellationToken.None)
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    if (isDone)
                        return true;
                    break;

                case "message_stop":
                    isDone = ProcessMessageStop(responseStreamChunkGetter, functionName, functionCalls, toolUseId, chatId, maxDeepAchived, CancellationToken.None)
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    if (isDone)
                        return true;
                    break;
            }

            return isDone;
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
                        type = "tool_use",
                        id = toolUseId,
                        name = functionName,
                        input = string.IsNullOrEmpty(inputJson)
                            ? new Dictionary<string, string>()
                            : (object)JObject.Parse(inputJson)
                    })
                ]
            };
        }

        protected override ChatMessage CreateResultMessage(string toolUseId, AiFunctionResult result, bool isError)
        {
            var contentData = new List<object>();

            var textValue = result.Content.OfType<TextContentItem>().FirstOrDefault()?.Text;
            if (!string.IsNullOrEmpty(textValue))
            {
                contentData.Add(new { type = "text", text = textValue });
            }

            var imageItem = result.Content.OfType<ImageContentItem>().FirstOrDefault();
            if (imageItem != null && !string.IsNullOrEmpty(imageItem.ImageInBase64))
            {
                contentData.Add(new
                {
                    type = "image",
                    source = new { type = "base64", media_type = MediaTypeNames.Image.Webp, data = imageItem.ImageInBase64 }
                });
            }

            var message = new ChatMessage
            {
                Role = MessageRole.eRoleUser,
                Content =
                [
                    ChatMessage.CreateJsonObject(new
                    {
                        type = "tool_result",
                        tool_use_id = toolUseId,
                        content = contentData,
                        is_error = isError
                    })
                ]
            };

            if (imageItem != null) message.Content.Add(imageItem);
            return message;
        }

        private static string GetEventType(string data)
        {
            try
            {
                var evt = System.Text.Json.JsonSerializer.Deserialize<EventType>(data);
                return evt?.Type ?? "";
            }
            catch (System.Text.Json.JsonException)
            {
                return "";
            }
        }

        private static void ProcessContentBlockStart(string eventData, ref string toolUseId, ref string functionName)
        {
            var contentBlock = System.Text.Json.JsonSerializer.Deserialize<ContentBlockStart>(eventData);
            if (contentBlock?.ContentBlock?.Type == "tool_use")
            {
                functionName = contentBlock.ContentBlock?.Name ?? "";
                toolUseId = contentBlock.ContentBlock?.Id ?? "";
            }
        }

        private static async Task<bool> ProcessContentBlockDelta(
            Func<ResponseStreamChunk, Task<bool>> responseStreamChunkGetter,
            string functionName,
            Dictionary<string, string> functionCalls,
            string contentData,
            CancellationToken cancellationToken)
        {
            var delta = System.Text.Json.JsonSerializer.Deserialize<ContentBlockDelta>(contentData);
            if (delta?.Delta?.Type == "text_delta")
            {
                return await responseStreamChunkGetter(new ResponseStreamChunk(delta.Delta.Text ?? "")).ConfigureAwait(false);
            }
            else if (delta?.Delta?.Type == "input_json_delta")
            {
                if (!functionCalls.ContainsKey(functionName))
                {
                    functionCalls[functionName] = "";
                }
                functionCalls[functionName] += delta.Delta.PartialJson ?? "";
            }

            return false;
        }

        private Task<bool> ProcessMessageStop(
            Func<ResponseStreamChunk, Task<bool>> responseStreamChunkGetter,
            string functionName,
            Dictionary<string, string> functionCalls,
            string toolUseId,
            string chatId,
            bool maxDeepAchived,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(functionName))
                return Task.FromResult(false);

            string parameters = functionCalls[functionName];
            return ProcessFunctionCall(functionName, parameters, toolUseId, chatId, responseStreamChunkGetter, maxDeepAchived, cancellationToken);
        }

        private static List<object> GetAnthropicMessages(IEnumerable<ChatMessage> messages)
        {
            var requestMessages = new List<object>();
            var listMessages = messages.ToList();
            for (int i = 0; i < listMessages.Count; ++i)
            {
                var message = listMessages[i];
                var values = ConvertChatMessageToAnthropicMessage(message, i == listMessages.Count - 1);
                requestMessages.Add(new
                {
                    role = message.Role == MessageRole.eRoleAI ? "assistant" : "user",
                    content = values
                });
            }

            return requestMessages;
        }

        private static List<object> ConvertChatMessageToAnthropicMessage(ChatMessage message, bool isLast)
        {
            var values = new List<object>();
            bool isFunctionResultAsJson = false;
            if (message.Role == MessageRole.eRoleUser)
            {
                var rawContent = message.Content.OfType<JsonObjectContentItem>().FirstOrDefault()?.JsonObject;
                if (rawContent != null)
                {
                    values.Add(rawContent);
                    isFunctionResultAsJson = true;
                }
            }

            if (!isFunctionResultAsJson)
            {
                var textContent = message.Content.OfType<TextContentItem>().FirstOrDefault();
                if (!string.IsNullOrEmpty(textContent?.Text))
                {
                    if (isLast)
                    {
                        values.Add(new
                        {
                            type = "text",
                            text = textContent.Text,
                            cache_control = new { type = "ephemeral" }
                        });
                    }
                    else
                    {
                        values.Add(new
                        {
                            type = "text",
                            text = textContent.Text
                        });
                    }
                }

                if (message.Role != MessageRole.eRoleAI)
                {
                    foreach (var imageItem in message.Content.OfType<ImageContentItem>())
                    {
                        if (!string.IsNullOrEmpty(imageItem.ImageInBase64))
                        {
                            values.Add(new
                            {
                                type = "image",
                                source = new
                                {
                                    type = "base64",
                                    media_type = MediaTypeNames.Image.Webp,
                                    data = imageItem.ImageInBase64
                                }
                            });
                        }
                    }
                }

                if (message.Role == MessageRole.eRoleAI)
                {
                    var rawContent = message.Content.OfType<JsonObjectContentItem>().FirstOrDefault()?.JsonObject;
                    if (rawContent != null)
                    {
                        values.Add(rawContent);
                    }
                }
            }

            return values;
        }
    }
}