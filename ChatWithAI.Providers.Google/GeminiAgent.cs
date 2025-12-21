using ChatWithAI.Contracts.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;

namespace ChatWithAI.Providers.Google
{
    internal sealed class GeminiAgent(
        string aiName,
        string systemMessage,
        bool enableFunctions,
        GeminiConfig config,
        IAiImagePainter? aiImagePainter,
        IAiFunctionsManager aiFunctionsManager,
        IHttpClientFactory httpClientFactory,
        ILogger logger) : IAiAgent
    {
        public string AiName => aiName;

        // Gemini API accepts snake_case but returns camelCase
        private static readonly JsonSerializerSettings s_requestJsonSettings = new()
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy()
            },
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new NormalizeLineEndingsConverter() }
        };

        private static readonly JsonSerializerSettings s_responseJsonSettings = new()
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            },
            NullValueHandling = NullValueHandling.Ignore
        };

        public async Task<ImageContentItem> GetImage(string imageDescription, string imageSize, string userId, CancellationToken cancellationToken = default)
        {
            if (aiImagePainter == null)
            {
                throw new InvalidOperationException("Image painter is not configured");
            }
            return await aiImagePainter.GetImage(imageDescription, imageSize, userId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> GetResponse(string userId, string setting, string question, string? data, CancellationToken cancellationToken = default)
        {
            var messages = new List<ChatMessageModel>
            {
                new() { Role = MessageRole.eRoleUser, Content = [ChatMessageModel.CreateText(question)] }
            };

            if (!string.IsNullOrEmpty(data))
            {
                messages[0].Content.Add(ChatMessageModel.CreateText($"\nData: {data}"));
            }

            // Disable functions to enable Google Search for simple responses
            await using var responseStream = await GetResponseStreamInternalAsync(userId, messages, enableFunctions: false, cancellationToken).ConfigureAwait(false);
            var sb = new StringBuilder();
            await foreach (var delta in responseStream.GetTextDeltasAsync(cancellationToken))
            {
                sb.Append(delta);
            }
            return sb.ToString();
        }

        public async Task<IAiStreamingResponse> GetResponseStreamAsync(
            string userId,
            IEnumerable<ChatMessageModel> messages,
            CancellationToken cancellationToken = default)
        {
            return await GetResponseStreamInternalAsync(userId, [.. messages], enableFunctions, cancellationToken).ConfigureAwait(false);
        }

        private Task<IAiStreamingResponse> GetResponseStreamInternalAsync(
            string userId,
            List<ChatMessageModel> messages,
            bool enableFunctions,
            CancellationToken cancellationToken)
        {
            var outputStream = new GeminiStreamingResponse();

            // Start background task - use CancellationToken.None for Task.Run
            // because we handle cancellation inside ProcessStreamingRequestAsync
            // and we need the task to complete normally to call Complete() on the stream
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessStreamingRequestAsync(userId, messages, outputStream, enableFunctions, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    logger.LogDebugMessage("Gemini request cancelled");
                }
                catch (Exception ex)
                {
                    logger.LogErrorMessage($"Gemini streaming error: {ex.Message}");
                    throw;
                }
                finally
                {
                    outputStream.Complete();
                }
            }, CancellationToken.None);

            return Task.FromResult<IAiStreamingResponse>(outputStream);
        }

        private async Task ProcessStreamingRequestAsync(
            string userId,
            List<ChatMessageModel> messages,
            GeminiStreamingResponse outputStream,
            bool enableFunctions,
            CancellationToken cancellationToken)
        {
            logger.LogDebugMessage("ProcessStreamingRequestAsync started");

            var conversationHistory = new List<GeminiContent>();

            // Add system instruction if present
            GeminiContent? systemInstruction = null;
            if (!string.IsNullOrEmpty(systemMessage))
            {
                systemInstruction = new GeminiContent
                {
                    Parts = [new GeminiPart { Text = systemMessage }]
                };
            }

            // Convert chat messages to Gemini format
            foreach (var message in messages)
            {
                var geminiContents = await ConvertToGeminiContentAsync(message, cancellationToken).ConfigureAwait(false);
                if (geminiContents != null)
                {
                    conversationHistory.AddRange(geminiContents);
                }
            }

            logger.LogDebugMessage($"Converted {conversationHistory.Count} messages to Gemini format");

            var allCollectedParts = new List<GeminiPart>();

            await ProcessResponse(userId, outputStream, enableFunctions, conversationHistory, systemInstruction, allCollectedParts, cancellationToken).ConfigureAwait(false);

            // Pass all collected parts to the stream so Chat can retrieve them later
            outputStream.SetCollectedParts(allCollectedParts);
        }

        private async Task ProcessResponse(string userId, GeminiStreamingResponse outputStream, bool enableFunctions, List<GeminiContent> conversationHistory, GeminiContent? systemInstruction, List<GeminiPart> allCollectedParts, CancellationToken cancellationToken)
        {
            // Function calling loop
            bool continueLoop = true;
            while (continueLoop)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var request = BuildRequest(conversationHistory, systemInstruction, enableFunctions);
                var collectedParts = new List<GeminiPart>();
                GeminiPart? currentPart = null;
                string? trailingSignature = null; // Track signature from empty/trailing chunks

                int chunkCount = 0;
                await foreach (var chunk in StreamRequestAsync(request, cancellationToken))
                {
                    chunkCount++;
                    if (chunk.Candidates == null || chunk.Candidates.Count == 0)
                    {
                        logger.LogDebugMessage($"Chunk {chunkCount}: no candidates");
                        continue;
                    }

                    var candidate = chunk.Candidates[0];
                    if (candidate.Content?.Parts == null)
                    {
                        logger.LogDebugMessage($"Chunk {chunkCount}: no parts, finishReason={candidate.FinishReason}");
                        continue;
                    }

                    foreach (var part in candidate.Content.Parts)
                    {
                        // 1. Handle Output Stream (User visible text)
                        if (part.Thought != true && part.FunctionCall == null && !string.IsNullOrEmpty(part.Text))
                        {
                            var text = (string)part.Text.Clone();
                            await outputStream.WriteTextAsync(text).ConfigureAwait(false);
                            logger.LogDebugMessage($"Chunk {chunkCount}: text ({part.Text.Length} chars)");
                        }
                        else if (part.Thought == true)
                        {
                            logger.LogDebugMessage($"Chunk {chunkCount}: thought ({part.Text?.Length ?? 0} chars)");
                        }
                        else if (part.FunctionCall != null)
                        {
                            logger.LogDebugMessage($"Chunk {chunkCount}: function call {part.FunctionCall.Name}, thoughtSignature={part.ThoughtSignature ?? "null"}");
                        }
                        else if (part.InlineData != null)
                        {
                            logger.LogDebugMessage($"Chunk {chunkCount}: inline data ({part.InlineData.MimeType})");
                        }
                        else if (string.IsNullOrEmpty(part.Text) && !string.IsNullOrEmpty(part.ThoughtSignature))
                        {
                            // Empty text part with signature - Gemini sends this at the end
                            logger.LogDebugMessage($"Chunk {chunkCount}: trailing signature part");
                        }

                        // 2. Capture trailing signature (may come in empty text part)
                        if (!string.IsNullOrEmpty(part.ThoughtSignature))
                        {
                            trailingSignature = part.ThoughtSignature;
                        }

                        // 3. Handle History Collection (Preserve Thoughts and Signatures)
                        if (part.FunctionCall != null)
                        {
                            // Function calls are distinct parts, flush current and add
                            if (currentPart != null)
                            {
                                collectedParts.Add(currentPart);
                                currentPart = null;
                            }
                            collectedParts.Add(part);
                            // Reset trailing signature since function call has its own
                            trailingSignature = null;
                        }
                        else if (part.InlineData != null)
                        {
                            // Inline data (images) are distinct parts, flush current and add
                            if (currentPart != null)
                            {
                                collectedParts.Add(currentPart);
                                currentPart = null;
                            }
                            collectedParts.Add(part);
                        }
                        else if (part.Thought == true)
                        {
                            // It is a thought chunk
                            if (currentPart != null && currentPart.Thought == true)
                            {
                                // Append to current thought part
                                currentPart.Text += part.Text;
                                // Update signature if present (usually on last chunk)
                                if (!string.IsNullOrEmpty(part.ThoughtSignature))
                                    currentPart.ThoughtSignature = part.ThoughtSignature;
                            }
                            else
                            {
                                if (currentPart != null)
                                {
                                    collectedParts.Add(currentPart);
                                }
                                currentPart = new GeminiPart { Thought = true, Text = part.Text, ThoughtSignature = part.ThoughtSignature };
                            }
                        }
                        else if (!string.IsNullOrEmpty(part.Text))
                        {
                            // It is a text chunk
                            if (currentPart != null && currentPart.Thought != true && currentPart.FunctionCall == null)
                            {
                                // Append to current text part
                                currentPart.Text += part.Text;
                                // Update signature if present
                                if (!string.IsNullOrEmpty(part.ThoughtSignature))
                                    currentPart.ThoughtSignature = part.ThoughtSignature;
                            }
                            else
                            {
                                if (currentPart != null)
                                {
                                    collectedParts.Add(currentPart);
                                }
                                currentPart = new GeminiPart { Text = part.Text, ThoughtSignature = part.ThoughtSignature };
                            }
                        }
                    }
                }

                // Flush remaining part
                if (currentPart != null)
                {
                    // Apply trailing signature if the current part doesn't have one
                    // This handles the case where Gemini sends signature in an empty trailing chunk
                    if (string.IsNullOrEmpty(currentPart.ThoughtSignature) && !string.IsNullOrEmpty(trailingSignature))
                    {
                        currentPart.ThoughtSignature = trailingSignature;
                    }
                    collectedParts.Add(currentPart);
                }
                // If no current part but we have collected parts, apply trailing signature to last text part
                else if (!string.IsNullOrEmpty(trailingSignature) && collectedParts.Count > 0)
                {
                    // Find the last non-function-call part to apply the signature
                    for (int i = collectedParts.Count - 1; i >= 0; i--)
                    {
                        var p = collectedParts[i];
                        if (p.FunctionCall == null && p.FunctionResponse == null && string.IsNullOrEmpty(p.ThoughtSignature))
                        {
                            p.ThoughtSignature = trailingSignature;
                            break;
                        }
                    }
                }

                // Add collected parts to the accumulator
                allCollectedParts.AddRange(collectedParts);

                logger.LogDebugMessage($"Stream finished: {chunkCount} chunks, {collectedParts.Count} parts collected");

                // Check for function calls in collected parts
                var functionCallParts = collectedParts.Where(p => p.FunctionCall != null).ToList();

                if (functionCallParts.Count > 0 && enableFunctions)
                {
                    // Add model response with function calls to history
                    conversationHistory.Add(new GeminiContent
                    {
                        Role = "model",
                        Parts = collectedParts
                    });

                    // Execute functions and add responses
                    var functionResponseParts = new List<GeminiPart>();
                    foreach (var fcPart in functionCallParts)
                    {
                        var functionCall = fcPart.FunctionCall!;
                        var result = await ExecuteFunctionAsync(userId, functionCall, cancellationToken).ConfigureAwait(false);
                        functionResponseParts.Add(new GeminiPart
                        {
                            FunctionResponse = new GeminiFunctionResponse
                            {
                                Name = functionCall.Name,
                                Response = result
                            }
                        });
                    }

                    // Add function responses to accumulator
                    allCollectedParts.AddRange(functionResponseParts);

                    conversationHistory.Add(new GeminiContent
                    {
                        Role = "user",
                        Parts = functionResponseParts
                    });

                    // Continue loop to get model's next response
                }
                else
                {
                    continueLoop = false;
                }
            }
        }

        private GeminiGenerateContentRequest BuildRequest(List<GeminiContent> contents, GeminiContent? systemInstruction, bool enableFunctions)
        {
            var request = new GeminiGenerateContentRequest
            {
                Contents = contents,
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = config.Temperature,
                    MaxOutputTokens = config.MaxTokens
                }
            };

            if (!string.Equals(config.ThinkingLevel, "none", StringComparison.OrdinalIgnoreCase))
            {
                request.GenerationConfig.ThinkingConfig = new GeminiThinkingConfig
                {
                    ThinkingLevel = config.ThinkingLevel,
                    IncludeThoughts = false
                };
            }

            if (systemInstruction != null)
            {
                request.SystemInstruction = systemInstruction;
            }

            // Add tools - Note: Gemini API doesn't support google_search + function_declarations together
            if (enableFunctions)
            {
                var functionsJson = aiFunctionsManager.Representation();
                var functionDeclarations = JsonConvert.DeserializeObject<List<GeminiFunctionDeclaration>>(functionsJson, s_responseJsonSettings);
                if (functionDeclarations != null && functionDeclarations.Count > 0)
                {
                    request.Tools = [new GeminiTool { FunctionDeclarations = functionDeclarations }];
                }
            }
            else
            {
                // Add Google Search only when function calling is disabled
                request.Tools = [new GeminiTool { GoogleSearch = new Dictionary<string, object>() }];
            }

            return request;
        }

        private async IAsyncEnumerable<GeminiGenerateContentResponse> StreamRequestAsync(
            GeminiGenerateContentRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var client = httpClientFactory.CreateClient("google_gemini_client");
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json)
            {
                CharSet = "utf-8"
            });

            var url = $"{config.ApiEndpoint}/models/{config.Model}:streamGenerateContent?alt=sse&key={config.ApiKey}";

            var jsonContent = JsonConvert.SerializeObject(request, s_requestJsonSettings);
            logger.LogDebugMessage($"Gemini request: {jsonContent}");

            using var httpContent = new StringContent(jsonContent, Encoding.UTF8, MediaTypeNames.Application.Json);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = httpContent };

            using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                logger.LogErrorMessage($"Gemini API error: {response.StatusCode} - {errorBody}");
                throw new HttpRequestException($"Gemini API error: {response.StatusCode} - {errorBody}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(line))
                    continue;

                var dataTag = "data:";
                if (!line.StartsWith(dataTag, StringComparison.Ordinal))
                    continue;

                var jsonData = line[dataTag.Length..].Trim();
                if (string.IsNullOrEmpty(jsonData))
                    continue;

                logger.LogDebugMessage($"SSE raw data: {jsonData[..Math.Min(500, jsonData.Length)]}");

                GeminiGenerateContentResponse? chunk = null;
                try
                {
                    chunk = JsonConvert.DeserializeObject<GeminiGenerateContentResponse>(jsonData, s_responseJsonSettings);
                }
                catch (JsonException ex)
                {
                    logger.LogErrorMessage($"Failed to parse SSE chunk: {ex.Message}");
                }

                if (chunk != null)
                {
                    yield return chunk;
                }
            }
        }

        private async Task<Dictionary<string, object>> ExecuteFunctionAsync(string userId, GeminiFunctionCall functionCall, CancellationToken cancellationToken)
        {
            try
            {
                var argsJson = functionCall.Args != null
                    ? JsonConvert.SerializeObject(functionCall.Args, s_requestJsonSettings)
                    : "{}";

                logger.LogDebugMessage($"Executing function: {functionCall.Name} with args: {argsJson}");

                var result = await aiFunctionsManager.Execute(
                    this,
                    functionCall.Name ?? string.Empty,
                    argsJson,
                    userId,
                    cancellationToken).ConfigureAwait(false);

                var responseContent = new Dictionary<string, object>();

                foreach (var item in result.Content)
                {
                    if (item is TextContentItem textItem && !string.IsNullOrEmpty(textItem.Text))
                    {
                        responseContent["text"] = textItem.Text;
                    }
                    else if (item is ImageContentItem imageItem)
                    {
                        var imageBase64 = await imageItem.GetImageBase64Async(cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(imageBase64))
                        {
                            responseContent["image_base64"] = imageBase64;
                        }
                        if (imageItem.ImageUrl != null)
                        {
                            responseContent["image_url"] = imageItem.ImageUrl.ToString();
                        }
                    }
                }

                if (result is AiFunctionErrorResult)
                {
                    responseContent["error"] = true;
                }

                return responseContent;
            }
            catch (Exception ex)
            {
                logger.LogErrorMessage($"Function execution error: {ex.Message}");
                return new Dictionary<string, object>
                {
                    ["error"] = true,
                    ["message"] = ex.Message
                };
            }
        }

        private static async Task<List<GeminiContent>?> ConvertToGeminiContentAsync(ChatMessageModel message, CancellationToken cancellationToken)
        {
            var result = new List<GeminiContent>();
            var currentParts = new List<GeminiPart>();

            // Default role based on message role
            string currentRole = message.Role switch
            {
                MessageRole.eRoleUser => "user",
                MessageRole.eRoleAI => "model",
                MessageRole.eRoleSystem => "user",
                MessageRole.eRoleTool => "user",
                _ => "user"
            };

            // Track if we've seen a function call - for parallel FC signature logic
            //bool hasFunctionCall = false;

            foreach (var contentItem in message.Content)
            {
                GeminiPart? part = null;
                string? partRole = null;

                switch (contentItem)
                {
                    case TextContentItem textItem when !string.IsNullOrEmpty(textItem.Text):
                        var textSignature = textItem.Signature;
                        part = new GeminiPart
                        {
                            Text = textItem.Text,
                            ThoughtSignature = textItem.Signature
                        };
                        break;

                    case ImageContentItem imageItem:
                        var imageData = await imageItem.GetImageBase64Async(cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(imageData))
                        {
                            part = new GeminiPart
                            {
                                InlineData = new GeminiInlineData
                                {
                                    MimeType = imageItem.MimeType,
                                    Data = imageData
                                },
                                ThoughtSignature = imageItem.Signature
                            };
                        }
                        break;

                    case AudioContentItem audioItem:
                        var audioData = await audioItem.GetAudioBase64Async(cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(audioData))
                        {
                            part = new GeminiPart
                            {
                                InlineData = new GeminiInlineData
                                {
                                    MimeType = audioItem.MimeType,
                                    Data = audioData
                                },
                                ThoughtSignature = audioItem.Signature
                            };
                        }
                        break;

                    case VideoContentItem videoItem:
                        var videoData = await videoItem.GetVideoBase64Async(cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(videoData))
                        {
                            part = new GeminiPart
                            {
                                InlineData = new GeminiInlineData
                                {
                                    MimeType = videoItem.MimeType,
                                    Data = videoData
                                },
                                ThoughtSignature = videoItem.Signature
                            };
                        }
                        break;

                    case DocumentContentItem documentItem:
                        var documentData = await documentItem.GetDocumentBase64Async(cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(documentData))
                        {
                            part = new GeminiPart
                            {
                                InlineData = new GeminiInlineData
                                {
                                    MimeType = documentItem.MimeType,
                                    Data = documentData
                                },
                                ThoughtSignature = documentItem.Signature
                            };
                        }
                        break;

                    case JsonObjectContentItem jsonItem:
                        // Handle both Dictionary<string, object> and JObject (from deserialization)
                        var jsonDict = jsonItem.JsonObject as Dictionary<string, object>;
                        if (jsonDict == null && jsonItem.JsonObject is Newtonsoft.Json.Linq.JObject jObj)
                        {
                            jsonDict = jObj.ToObject<Dictionary<string, object>>();
                        }

                        if (jsonDict != null && jsonDict.TryGetValue("function_call", out var fcObj))
                        {
                            var json = JsonConvert.SerializeObject(fcObj);
                            var def = new { name = "", args = new Dictionary<string, object>() };
                            var obj = JsonConvert.DeserializeAnonymousType(json, def);
                            if (obj != null)
                            {
                                // Function call signatures are mandatory for Gemini 3 on the FIRST function call (if using parallel function calling)
                                // Alternatively, each function call MUST preserve signatures for subsequent function calls
                                var fcSignature = jsonItem.Signature;

                                part = new GeminiPart
                                {
                                    FunctionCall = new GeminiFunctionCall
                                    {
                                        Name = obj.name,
                                        Args = obj.args
                                    },
                                    ThoughtSignature = fcSignature
                                };
                                partRole = "model";
                            }
                        }
                        else if (jsonDict != null && jsonDict.TryGetValue("function_response", out var frObj))
                        {
                            var json = JsonConvert.SerializeObject(frObj);
                            var def = new { name = "", response = new Dictionary<string, object>() };
                            var obj = JsonConvert.DeserializeAnonymousType(json, def);
                            if (obj != null)
                            {
                                part = new GeminiPart
                                {
                                    FunctionResponse = new GeminiFunctionResponse
                                    {
                                        Name = obj.name,
                                        Response = obj.response
                                    }
                                    // Function responses are generated by user and always don't have signatures
                                };
                                partRole = "user";
                            }
                        }
                        break;
                }

                if (part != null)
                {
                    // Determine effective role for this part
                    var effectiveRole = partRole ?? currentRole;

                    // If role switches, flush current parts and start new content
                    if (effectiveRole != currentRole && currentParts.Count > 0)
                    {
                        result.Add(new GeminiContent { Role = currentRole, Parts = [.. currentParts] });
                        currentParts.Clear();
                    }

                    currentRole = effectiveRole;
                    currentParts.Add(part);
                }
            }

            if (currentParts.Count > 0)
            {
                result.Add(new GeminiContent { Role = currentRole, Parts = currentParts });
            }

            return result.Count > 0 ? result : null;
        }
    }

    #region NormalizeLineEndingsConverter

    internal sealed class NormalizeLineEndingsConverter : JsonConverter<string>
    {
        public override string? ReadJson(JsonReader reader, Type objectType, string? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            return reader.Value as string;
        }

        public override void WriteJson(JsonWriter writer, string? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                writer.WriteValue(value.Replace("\r\n", "\n").Replace("\r", "\n"));
            }
        }
    }

    #endregion
}
