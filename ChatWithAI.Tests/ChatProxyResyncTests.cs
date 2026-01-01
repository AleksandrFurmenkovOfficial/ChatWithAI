using ChatWithAI.Contracts.Configs;
using ChatWithAI.Contracts.Model;
using ChatWithAI.Core.StateMachine;
using System.Globalization;
using Xunit.Abstractions;
using CoreChatState = ChatWithAI.Core.ChatState;

namespace ChatWithAI.Tests;

public sealed class ChatProxyCacheResetTests : IDisposable
{
    private readonly MockLogger _logger;
    private readonly MockMessenger _messenger;
    private readonly MockAiAgentFactory _aiAgentFactory;
    private readonly ChatCache _cache;

    public ChatProxyCacheResetTests(ITestOutputHelper output)
    {
        _logger = new MockLogger(output);
        _messenger = new MockMessenger();
        _aiAgentFactory = new MockAiAgentFactory();
        _cache = new ChatCache(TimeSpan.FromMinutes(60), _logger);
    }

    public void Dispose()
    {
        _cache?.Dispose();
    }

    [Fact]
    public void Constructor_ClearsCachedState()
    {
        var config = new AppConfig { ChatCacheAliveInMinutes = 60 };
        var chatId = Guid.NewGuid().ToString();
        var mode = new ChatMode { AiName = "Test_TestMode", AiSettings = "" };

        var state = new CoreChatState(chatId, _messenger.MaxTextMessageLen(), _messenger.MaxPhotoMessageLen());
        var userMessage = new ChatMessageModel([ChatMessageModel.CreateText("Hello")], MessageRole.eRoleUser, "user");
        state.History.AddUserMessages([userMessage]);
        _cache.Set($"{chatId}_state", state, TimeSpan.FromMinutes(60));

        Assert.True(_cache.Contains($"{chatId}_state"));

        using var chat = new ChatProxy(config, chatId, mode, _aiAgentFactory, _messenger, _logger, _cache, false);

        Assert.False(_cache.Contains($"{chatId}_state"));
    }

    private sealed class MockAiAgentFactory : IAiAgentFactory
    {
        public IAiAgent CreateAiAgent(string aiName, string aiSettings, bool useTools, bool imageOnlyMode, bool useFlash)
        {
            return new MockAiAgent(aiName);
        }
    }

    private sealed class MockAiAgent : IAiAgent
    {
        private readonly string _response;

        public MockAiAgent(string aiName, string response = "Mock streaming response")
        {
            AiName = aiName;
            _response = response;
        }

        public string AiName { get; }
        public bool GetResponseCalled { get; private set; }

        public Task<string> GetResponse(string userId, string setting, string question, string? data, CancellationToken cancellationToken = default)
        {
            return Task.FromResult("Mock response");
        }

        public Task<IAiStreamingResponse> GetResponseStreamAsync(string userId, IEnumerable<ChatMessageModel> messages, CancellationToken cancellationToken = default)
        {
            GetResponseCalled = true;
            IAiStreamingResponse response = new SingleTextStreamingResponse(_response);
            return Task.FromResult(response);
        }

        public Task<ImageContentItem> GetImage(string imageDescription, string imageSize, string userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ImageContentItem());
        }
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

    private sealed class MockMessenger : IMessenger
    {
        private int _nextMessageId = 1000;

        public int MaxTextMessageLen() => 4096;
        public int MaxPhotoMessageLen() => 1024;

        public Task<bool> DeleteMessage(string chatId, MessageId messageId) => Task.FromResult(true);

        public Task<string> SendTextMessage(string chatId, MessengerMessageDTO message, IEnumerable<ActionId>? messageActionIds = null)
            => Task.FromResult(Interlocked.Increment(ref _nextMessageId).ToString(CultureInfo.InvariantCulture));

        public Task<MessengerEditResult> EditTextMessage(string chatId, MessageId messageId, string content, IEnumerable<ActionId>? messageActionIds = null)
            => Task.FromResult(MessengerEditResult.Success);

        public Task<string> SendPhotoMessage(string chatId, MessengerMessageDTO message, IEnumerable<ActionId>? messageActionIds = null)
            => Task.FromResult(Interlocked.Increment(ref _nextMessageId).ToString(CultureInfo.InvariantCulture));

        public Task<MessengerEditResult> EditPhotoMessage(string chatId, MessageId messageId, string caption, IEnumerable<ActionId>? messageActionIds = null)
            => Task.FromResult(MessengerEditResult.Success);
    }

    private sealed class MockLogger : ILogger
    {
        private readonly ITestOutputHelper? _output;
        public MockLogger(ITestOutputHelper? output) => _output = output;
        public void LogInfoMessage(string message) => _output?.WriteLine($"[INFO] {message}");
        public void LogDebugMessage(string message) => _output?.WriteLine($"[DEBUG] {message}");
        public void LogErrorMessage(string message) => _output?.WriteLine($"[ERROR] {message}");
        public void LogException(Exception e) => _output?.WriteLine($"[EXCEPTION] {e}");
    }
}
