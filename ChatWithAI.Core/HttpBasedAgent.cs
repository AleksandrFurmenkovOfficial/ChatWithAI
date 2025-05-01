using Newtonsoft.Json;
using System.Diagnostics;
using System.Net.Mime;
using System.Text;

namespace ChatWithAI.Core
{
    public abstract class HttpBasedAgent(
        string aiName,
        string systemMessage,
        bool enableFunctions,
        IAiImagePainter? aiImagePainter,
        IAiFunctionsManager aiFunctionsManager) : IAiAgent, IDisposable // .NET 8.0 primary constructor, do not rewrite to older version!
    {
#pragma warning disable CA1051 // Do not declare visible instance fields
        protected readonly HttpClient m_httpClient = new();
        protected readonly string m_aiName = aiName;
        protected readonly string m_systemMessage = systemMessage;
        protected readonly bool m_enableFunctions = enableFunctions;
        protected readonly IAiImagePainter? m_aiImagePainter = aiImagePainter;
        protected readonly IAiFunctionsManager m_aiFunctionsManager = aiFunctionsManager;
        protected readonly bool m_useCache = true;
#pragma warning restore CA1051 // Do not declare visible instance fields

        public string AiName => m_aiName;

        public void Dispose()
        {
            m_httpClient.Dispose();
            GC.SuppressFinalize(this);
        }

        protected abstract string GetStreamUrl();

        protected abstract string GetSingleshotUrl();

        protected abstract string GetJsonPayload(IEnumerable<ChatMessage> messages, string systemMessage, bool useStream = false, bool useTools = false, bool useCache = false);

        protected abstract string GetTextFromResponse(string text);

        protected abstract bool SkipLine(string line);

        protected abstract bool ProcessStreamChunck(
            Func<ResponseStreamChunk, Task<bool>> responseStreamChunkGetter,
            ref string functionName,
            ref Dictionary<string, string> functionCalls,
            ref string toolUseId,
            string chatId,
            string chunk,
            bool maxDeepAchived);

        protected virtual JsonSerializerSettings GetJsonSerializerSettings()
        {
            return new JsonSerializerSettings()
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore
            };
        }

        public Task<ImageContentItem> GetImage(string imageDescription, string imageSize, string userId, CancellationToken cancellationToken = default)
        {
            if (m_aiImagePainter == null)
                throw new InvalidOperationException("m_aiImagePainter == null");

            return m_aiImagePainter.GetImage(imageDescription, imageSize, userId, cancellationToken);
        }

        protected virtual List<ChatMessage> GetProcessedMessages(IEnumerable<ChatMessage> messages)
        {
            var withoutAnswerStub = messages.ToList();
            while (withoutAnswerStub.Count > 0 && withoutAnswerStub.Last().Role == MessageRole.eRoleAI)
            {
                withoutAnswerStub.RemoveAt(withoutAnswerStub.Count - 1);
            }

            return withoutAnswerStub;
        }

        public async Task<string> GetResponse(string setting, string question, string? data, CancellationToken cancellationToken = default)
        {
            var messages = new List<ChatMessage> { new() { Role = MessageRole.eRoleUser, Content = [ChatMessage.CreateText(data != null ? $"{question}\n{data}" : question)] } };
            string jsonRequest = GetJsonPayload(messages, setting, useStream: false, useTools: false, useCache: m_useCache);
            using var content = new StringContent(jsonRequest, Encoding.UTF8, MediaTypeNames.Application.Json);
            using var request = new HttpRequestMessage(HttpMethod.Post, GetSingleshotUrl()) { Content = content };
            using var response = await m_httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new HttpRequestException($"Request failed: {response.StatusCode}, Content: {responseContent}");
            }

            string result = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return GetTextFromResponse(result);
        }

        protected static bool IsMaxDeepFromUserRequest(List<ChatMessage> messages, int maxDeep = 10)
        {
            int deep = 0;
            for (int i = messages.Count - 1; i >= 0; --i)
            {
                if (messages[i].Role == MessageRole.eRoleUser && messages[i].Id != ChatMessage.InternalMessageId)
                {
                    break;
                }

                ++deep;
            }

            return deep > maxDeep;
        }

        public async Task GetResponse(string chatId,
            IEnumerable<ChatMessage> messages,
            Func<ResponseStreamChunk, Task<bool>> responseStreamChunkGetter,
            CancellationToken cancellationToken = default)
        {
            bool isCancelled = false;
            bool maxDeepAchived = IsMaxDeepFromUserRequest(messages.ToList());
            try
            {
                var jsonRequest = GetJsonPayload(messages, m_systemMessage, useStream: true, useTools: m_enableFunctions, useCache: m_useCache);

                // await File.WriteAllTextAsync($"D:\\jsonRequest{++resp}.json", jsonRequest, cancellationToken).ConfigureAwait(false);

                using var content = new StringContent(jsonRequest, Encoding.UTF8, MediaTypeNames.Application.Json);
                using var request = new HttpRequestMessage(HttpMethod.Post, GetStreamUrl()) { Content = content };
                using var response = await m_httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    throw new HttpRequestException($"Request failed: {response.StatusCode}, Content: {responseContent}");
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream);

                string functionName = "";
                string toolUseId = "";
                var functionCalls = new Dictionary<string, string>();

                while (!reader.EndOfStream)
                {
                    var chunk = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    Debug.WriteLine(chunk);
                    if (string.IsNullOrEmpty(chunk) || SkipLine(chunk))
                    {
                        continue;
                    }

                    isCancelled = ProcessStreamChunck(responseStreamChunkGetter, ref functionName, ref functionCalls, ref toolUseId, chatId, chunk, maxDeepAchived);
                    if (isCancelled)
                        break;
                }
            }
            finally
            {
                if (!isCancelled)
                {
                    await responseStreamChunkGetter(new LastResponseStreamChunk()).ConfigureAwait(false);
                }
            }
        }

        protected async Task<bool> ProcessFunctionCall(
            string functionName,
            string parameters,
            string toolUseId,
            string chatId,
            Func<ResponseStreamChunk, Task<bool>> responseStreamChunkGetter,
            bool maxDeepAchived,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(functionName))
                return false;

            AiFunctionResult functionResult;
            try
            {
                if (maxDeepAchived)
                {
                    throw new InvalidOperationException($"Too many function calls in a row. Please ask the user to continue!");
                }

                functionResult = await m_aiFunctionsManager
                    .Execute(this, functionName, parameters, chatId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                functionResult = new AiFunctionResult(
                    $"Exception: Cannot call function {functionName} ({parameters}); " +
                    "Possible issues:\n" +
                    "1. Function name is incorrect\n" +
                    "2. Wrong arguments provided\n" +
                    "3. Internal function error\n" +
                    "4. Too many function calls in a row\n" +
                    $"Exception message: {e.Message}\n" +
                    "Note: Always inform users directly and honestly about function issues.");
            }

            var resultMessages = CreateFunctionResultMessages(parameters, toolUseId, functionName, functionResult, functionResult is AiFunctionErrorResult);
            return await responseStreamChunkGetter(new ResponseStreamChunk(resultMessages)).ConfigureAwait(false);
        }

        protected virtual List<ChatMessage> CreateFunctionResultMessages(
            string inputJson,
            string toolUseId,
            string functionName,
            AiFunctionResult result,
            bool isError = false)
        {
            return [CreateCallMessage(inputJson, toolUseId, functionName), CreateResultMessage(toolUseId, result, isError)];
        }

        protected abstract ChatMessage CreateCallMessage(string inputJson, string toolUseId, string functionName);

        protected abstract ChatMessage CreateResultMessage(string toolUseId, AiFunctionResult result, bool isError);
    }
}