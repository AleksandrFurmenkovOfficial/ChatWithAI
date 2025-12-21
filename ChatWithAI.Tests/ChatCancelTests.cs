using ChatWithAI.Contracts.Configs;
using ChatWithAI.Contracts.Model;
using ChatWithAI.Core.StateMachine;
using System.Globalization;
using Xunit.Abstractions;
using ChatState = ChatWithAI.Core.ChatState;

namespace ChatWithAI.Tests
{
    public class ChatCancelTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly MockLogger _logger;
        private readonly MockMessenger _messenger;
        private readonly MockAiAgentFactory _aiAgentFactory;
        private readonly ChatCache _cache;
        private readonly Chat _chat;

        public ChatCancelTests(ITestOutputHelper output)
        {
            _output = output;
            _logger = new MockLogger(output);
            _messenger = new MockMessenger();
            _aiAgentFactory = new MockAiAgentFactory();
            _cache = new ChatCache(TimeSpan.FromMinutes(60), _logger);

            var config = new AppConfig { ChatCacheAliveInMinutes = 60 };
            _chat = new Chat(config, Guid.NewGuid().ToString(), _aiAgentFactory, _messenger, _logger, _cache, false);
            _chat.SetModeAsync(new ChatMode { AiName = "Test_TestMode", AiSettings = "" });
        }

        public void Dispose()
        {
            _chat?.Dispose();
            _cache?.Dispose();
        }

        private ChatState GetState()
        {
            // Access internal state via cache or reflection if needed, 
            // but for now we can rely on public API or cache directly if we know the key.
            // Key is $"{Id}_state"
            var key = $"{_chat.Id}_state";
            return _cache.Get<ChatState>(key);
        }

        [Fact]
        public async Task ContinueResponseAsync_WhenCancelled_PreservesOriginalMessage()
        {
            // 1. Setup initial state with a User message and an AI message (the one to continue)
            var userMsg = new ChatMessageModel([ChatMessageModel.CreateText("Hello")], MessageRole.eRoleUser, "User");
            var aiMsg = new ChatMessageModel([ChatMessageModel.CreateText("Start of answer...")], MessageRole.eRoleAI, "AI");

            // Add messages manually to history to simulate existing state
            await _chat.AddUserMessagesToChatHistoryAsync([userMsg]);

            // Hack: We need to add AI message to the same turn.
            // But public API doesn't expose "AddAssistantMessage".
            // We can get state and modify it directly since it's an object reference in cache (in-memory).
            var state = GetState();
            state.History.AddAssistantMessage(aiMsg);

            // Verify initial state
            Assert.Single(state.History.Turns);
            Assert.Equal(2, state.History.Turns[0].Count); // User + AI
            var initialLastMsg = state.History.GetLastAssistantMessage();
            Assert.NotNull(initialLastMsg);
            Assert.Equal(aiMsg.Id, initialLastMsg.Id);

            // 2. Configure Mock Agent to simulate delay then cancellation
            var agent = new MockAiAgent();
            agent.GetResponseStreamAsyncFunc = async (chatId, history, ct) =>
            {
                // Wait a bit to ensure we are "running"
                await Task.Delay(100, ct);
                // Throw cancellation
                throw new OperationCanceledException();
            };
            _aiAgentFactory.NextAgentToCreate = agent;

            // Re-initialize chat mode to pick up the new agent
            await _chat.SetModeAsync(new ChatMode { AiName = "Test_TestMode", AiSettings = "" });

            // 3. Run ContinueResponseAsync with a cancellable token
            // actually the agent throws cancellation, so we don't necessarily need to cancel the token passed in,
            // but let's cancel the token to be realistic.
            using var cts = new CancellationTokenSource();

            // Start the task
            var task = _chat.ContinueResponseAsync(cts.Token);

            // Cancel after a short delay
            cts.CancelAfter(50);

            // Wait for completion
            var result = await task;

            // 4. Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(ChatTrigger.UserCancel, result.NextTrigger);

            // Verify History
            // The "Please continue" system message should be gone (handled by ContinueOnCleanup)
            // The NEW partial message should be gone (handled by our fix)
            // The ORIGINAL AI message should BE THERE (this is what the fix protects)

            var lastMsg = state.History.GetLastAssistantMessage();
            Assert.NotNull(lastMsg);
            Assert.Equal(aiMsg.Id, lastMsg.Id);
            Assert.IsType<TextContentItem>(lastMsg.Content[0]);
            Assert.Equal("Start of answer...", ((TextContentItem)lastMsg.Content[0]).Text);

            // Verify turn count / message count
            Assert.Single(state.History.Turns);
            // Should be User + Original AI. "Please continue" and "New Partial" should be gone.
            Assert.Equal(2, state.History.Turns[0].Count);
        }
        // ... previous content ...

        #region Mock Classes

        private class MockAiAgentFactory : IAiAgentFactory
        {
            public MockAiAgent? LastCreatedAgent { get; set; }
            public MockAiAgent? NextAgentToCreate { get; set; }

            public IAiAgent CreateAiAgent(string aiName, string aiSettings, bool useTools, bool imageOnlyMode, bool useFlash)
            {
                var agent = NextAgentToCreate ?? new MockAiAgent(aiName);
                LastCreatedAgent = agent;
                return agent;
            }
        }

        private class MockAiAgent : IAiAgent
        {
            public string AiName { get; } = "MockAI";

            public Func<string, IEnumerable<ChatMessageModel>, CancellationToken, Task<IAiStreamingResponse>>? GetResponseStreamAsyncFunc { get; set; }

            public MockAiAgent(string aiName = "MockAI")
            {
                AiName = aiName;
            }

            public Task<string> GetResponse(string userId, string setting, string question, string? data, CancellationToken cancellationToken = default)
            {
                return Task.FromResult("Mock response");
            }

            public Task<IAiStreamingResponse> GetResponseStreamAsync(string chatId, IEnumerable<ChatMessageModel> messages, CancellationToken cancellationToken = default)
            {
                if (GetResponseStreamAsyncFunc != null)
                {
                    return GetResponseStreamAsyncFunc(chatId, messages, cancellationToken);
                }

                var mockResponse = "Mock streaming response";
                IAiStreamingResponse response = new SingleTextStreamingResponse(mockResponse);
                return Task.FromResult(response);
            }

            public Task<ImageContentItem> GetImage(string imageDescription, string imageSize, string userId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new ImageContentItem());
            }

            public void Dispose() { }
        }

        private sealed class SingleTextStreamingResponse : IAiStreamingResponse
        {
            private readonly string _text;

            public SingleTextStreamingResponse(string text) => _text = text ?? string.Empty;

            public IAsyncEnumerable<string> GetTextDeltasAsync(CancellationToken cancellationToken = default)
            {
                return Stream();

                async IAsyncEnumerable<string> Stream()
                {
                    yield return _text;
                    await Task.CompletedTask;
                }
            }

            public List<ContentItem>? GetStructuredContent() => null;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private class MockMessenger : IMessenger
        {
            private int _nextMessageId = 1000;

            public int MaxTextMessageLen() => 4096;
            public int MaxPhotoMessageLen() => 1024;

            public Task<bool> DeleteMessage(string chatId, MessageId messageId) => Task.FromResult(true);

            public Task<string> SendTextMessage(string chatId, MessengerMessageDTO message, IEnumerable<ActionId>? messageActionIds = null)
                => Task.FromResult(Interlocked.Increment(ref _nextMessageId).ToString(CultureInfo.InvariantCulture));

            public Task EditTextMessage(string chatId, MessageId messageId, string content, IEnumerable<ActionId>? messageActionIds = null)
                => Task.CompletedTask;

            public Task<string> SendPhotoMessage(string chatId, MessengerMessageDTO message, IEnumerable<ActionId>? messageActionIds = null)
                => Task.FromResult(Interlocked.Increment(ref _nextMessageId).ToString(CultureInfo.InvariantCulture));

            public Task EditPhotoMessage(string chatId, MessageId messageId, string caption, IEnumerable<ActionId>? messageActionIds = null)
                => Task.CompletedTask;
        }

        private class MockLogger : ILogger
        {
            private readonly ITestOutputHelper? _output;
            public MockLogger(ITestOutputHelper? output) => _output = output;
            public void LogInfoMessage(string message) => _output?.WriteLine($"[INFO] {message}");
            public void LogDebugMessage(string message) => _output?.WriteLine($"[DEBUG] {message}");
            public void LogErrorMessage(string message) => _output?.WriteLine($"[ERROR] {message}");
            public void LogException(Exception e) => _output?.WriteLine($"[EXCEPTION] {e}");
        }

        #endregion
    }
}
