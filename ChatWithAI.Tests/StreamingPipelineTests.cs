using ChatWithAI.Contracts.Configs;
using ChatWithAI.Core.StateMachine;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Xunit.Abstractions;
using ChatMessage = ChatWithAI.Contracts.Model.ChatMessageModel;

namespace ChatWithAI.Tests;

/// <summary>
/// Load tests for the streaming pipeline to detect race conditions.
/// </summary>
public class StreamingPipelineTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly MockLogger _logger;
    private readonly TrackingMessenger _messenger;
    private readonly DelayableAiAgentFactory _aiAgentFactory;
    private readonly ChatCache _cache;
    private readonly Chat _chat;
    private readonly ChatStateMachine _stateMachine;

    public StreamingPipelineTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = new MockLogger(output);
        _messenger = new TrackingMessenger();
        _aiAgentFactory = new DelayableAiAgentFactory();
        _cache = new ChatCache(TimeSpan.FromMinutes(60), _logger);

        var config = new AppConfig { ChatCacheAliveInMinutes = 60 };
        _chat = new Chat(config, Guid.NewGuid().ToString(), _aiAgentFactory, _messenger, _logger, _cache, false);
        _chat.SetModeAsync(new ChatMode { AiName = "Test_TestMode", AiSettings = "" });

        _stateMachine = new ChatStateMachine(_chat, _logger);
    }

    public void Dispose()
    {
        _stateMachine?.Dispose();
        _cache?.Dispose();
        _chat?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Log(string message) => _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

    #region 1. Basic Content Integrity Tests

    [Fact]
    public async Task StreamReader_Reads_AllContent_Simple()
    {
        Log("TEST START: StreamReader_Reads_AllContent_Simple");

        const string expectedText = "Hello, this is a test response from the AI.";
        _aiAgentFactory.ConfigureResponse(expectedText);

        await SendMessageAndRequestResponse();

        // Verify the final UI content matches
        var finalContent = _messenger.GetFinalTextContent();
        Assert.Equal(expectedText, finalContent);

        Log("TEST PASSED");
    }

    [Fact]
    public async Task StreamReader_Reads_AllContent_Long()
    {
        Log("TEST START: StreamReader_Reads_AllContent_Long");

        // Create text longer than MessageUpdateStepInCharsCount (168)
        var sb = new StringBuilder();
        for (int i = 0; i < 50; i++)
        {
            sb.Append($"This is line {i} of a longer response. ");
        }
        var expectedText = sb.ToString();
        _aiAgentFactory.ConfigureResponse(expectedText);

        await SendMessageAndRequestResponse();

        // Verify the final UI content matches (may be split across messages)
        var finalContent = _messenger.GetAllTextContent();
        Assert.Equal(expectedText, finalContent);

        Log("TEST PASSED");
    }

    #endregion

    #region 3. Load/Stress Tests

    [Fact]
    public async Task StreamReader_HighLoad_ManyChunks_NoRace()
    {
        Log("TEST START: StreamReader_HighLoad_ManyChunks_NoRace");

        // Generate many small chunks
        var chunks = new List<(string, int)>();
        var expectedBuilder = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            var text = $"Chunk{i} ";
            expectedBuilder.Append(text);
            chunks.Add((text, 5)); // Small delay between chunks
        }
        _aiAgentFactory.ConfigureChunkedResponse([.. chunks]);

        await SendMessageAndRequestResponse();

        var expectedText = expectedBuilder.ToString();
        var finalContent = _messenger.GetAllTextContent();
        Assert.Equal(expectedText, finalContent);

        Log("TEST PASSED");
    }

    [Fact]
    public async Task StreamReader_MultipleRequests_NoStateLeakage()
    {
        Log("TEST START: StreamReader_MultipleRequests_NoStateLeakage");

        for (int round = 0; round < 5; round++)
        {
            _messenger.Reset();
            var expectedText = $"Response for round {round}";
            _aiAgentFactory.ConfigureResponse(expectedText);

            // Must add messages first before requesting response
            var messages = new List<ChatMessage>
            {
                new([ChatMessage.CreateText($"Question {round}")], MessageRole.eRoleUser, "testuser")
            };
            await _stateMachine.FireAsync(ChatTrigger.UserAddMessages, new AddMessagesContext(messages), default);
            await _stateMachine.FireAsync(ChatTrigger.UserRequestResponse,
                new CancellableContext(CancellationToken.None), default);

            var finalContent = _messenger.GetFinalTextContent();
            Assert.Equal(expectedText, finalContent);
        }

        Log("TEST PASSED");
    }

    #endregion

    #region 4. Cancellation Tests

    [Fact]
    public async Task StreamReader_Cancellation_NoHang()
    {
        Log("TEST START: StreamReader_Cancellation_NoHang");

        // Configure slow response
        var chunks = new[]
        {
            ("Starting", 50),
            ("...", 2000),  // Long delay
            (" done", 50)
        };
        _aiAgentFactory.ConfigureChunkedResponse(chunks);

        using var cts = new CancellationTokenSource();

        // Start request
        var requestTask = _stateMachine.FireAsync(ChatTrigger.UserRequestResponse,
            new CancellableContext(cts.Token), default);

        // Cancel after short delay
        await Task.Delay(200);
        cts.Cancel();

        // Request should complete (either cancelled or finished)
        var timeoutTask = Task.Delay(3000);
        var completedTask = await Task.WhenAny(requestTask, timeoutTask);

        Assert.True(completedTask == requestTask, "Request should complete, not hang");

        Log("TEST PASSED");
    }

    #endregion

    #region 5. Race Condition Detection Tests

    /// <summary>
    /// Verifies that each UI update contains all content from previous updates (monotonically increasing).
    /// Race condition symptom: content shrinks or middle parts disappear between updates.
    /// </summary>
    [Fact]
    public async Task StreamReader_ContentMonotonicallyIncreasing()
    {
        Log("TEST START: StreamReader_ContentMonotonicallyIncreasing");

        // Configure chunked response that builds up gradually
        var chunks = new List<(string, int)>();
        var content = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuvwxyz!@#$%^&*()_+-=[]{}|;':\",./<>?";
        for (int i = 0; i < content.Length; i += 5)
        {
            var chunk = content.Substring(i, Math.Min(5, content.Length - i));
            chunks.Add((chunk, 10)); // Small delay
        }
        _aiAgentFactory.ConfigureChunkedResponse([.. chunks]);

        await SendMessageAndRequestResponse();

        // Get ordered updates and verify monotonicity
        var orderedUpdates = _messenger.GetOrderedUpdates();
        Log($"Total updates received: {orderedUpdates.Count}");

        string previousContent = "";
        int updateIndex = 0;
        foreach (var update in orderedUpdates.Where(u => !string.IsNullOrEmpty(u.Content) && u.Content != "Думаю... ⌛"))
        {
            // Each update should START WITH the previous content (or be a new message segment)
            // For streaming within a single message, content should grow monotonically
            if (!string.IsNullOrEmpty(previousContent) && update.Content.Length >= previousContent.Length)
            {
                bool isPrefix = update.Content.StartsWith(previousContent);
                if (!isPrefix && update.Content.Length > 0)
                {
                    Log($"POTENTIAL RACE at update {updateIndex}: prev='{previousContent}' ({previousContent.Length} chars), current='{update.Content}' ({update.Content.Length} chars)");
                    // Check if content was lost in the middle
                    var missingChars = previousContent.Except(update.Content).ToList();
                    if (missingChars.Count > 0)
                    {
                        Assert.Fail($"Content shrunk at update {updateIndex}: missing chars: {string.Join(",", missingChars)}");
                    }
                }
            }
            previousContent = update.Content;
            updateIndex++;
        }

        // Verify final content is complete
        var finalContent = _messenger.GetAllTextContent();
        Assert.Equal(content, finalContent);

        Log("TEST PASSED");
    }

    /// <summary>
    /// Verifies that the SHA256 checksum of source content matches final UI content.
    /// </summary>
    [Fact]
    public async Task StreamReader_FinalContentMatchesChecksum()
    {
        Log("TEST START: StreamReader_FinalContentMatchesChecksum");

        // Generate substantial content
        var sb = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            sb.Append($"Line {i:D3}: The quick brown fox jumps over the lazy dog. ");
        }
        var expectedText = sb.ToString();
        var expectedHash = ComputeSha256(expectedText);

        _aiAgentFactory.ConfigureResponse(expectedText);

        await SendMessageAndRequestResponse();

        var finalContent = _messenger.GetAllTextContent();
        var actualHash = ComputeSha256(finalContent);

        Log($"Expected hash: {expectedHash}");
        Log($"Actual hash: {actualHash}");
        Log($"Expected length: {expectedText.Length}, Actual length: {finalContent.Length}");

        if (expectedHash != actualHash)
        {
            // Find where content differs
            var minLen = Math.Min(expectedText.Length, finalContent.Length);
            for (int i = 0; i < minLen; i++)
            {
                if (expectedText[i] != finalContent[i])
                {
                    Log($"First difference at position {i}: expected '{expectedText[i]}', got '{finalContent[i]}'");
                    Log($"Context: ...{expectedText.Substring(Math.Max(0, i - 20), Math.Min(40, expectedText.Length - Math.Max(0, i - 20)))}...");
                    break;
                }
            }
        }

        Assert.Equal(expectedHash, actualHash);

        Log("TEST PASSED");
    }

    /// <summary>
    /// Stress test with 1000 rapid tiny chunks to expose race conditions.
    /// </summary>
    [Fact]
    public async Task StreamReader_RapidChunks_StressTest()
    {
        Log("TEST START: StreamReader_RapidChunks_StressTest");

        var chunks = new List<(string, int)>();
        var expectedBuilder = new StringBuilder();
        var random = new Random(42); // Fixed seed for reproducibility

        for (int i = 0; i < 1000; i++)
        {
            var text = $"{i:D4}";
            expectedBuilder.Append(text);
            // Random delay between 0-5ms to create timing variations
            chunks.Add((text, random.Next(0, 6)));
        }
        _aiAgentFactory.ConfigureChunkedResponse([.. chunks]);

        await SendMessageAndRequestResponse();

        var expectedText = expectedBuilder.ToString();
        var finalContent = _messenger.GetAllTextContent();

        Log($"Expected length: {expectedText.Length}, Actual length: {finalContent.Length}");

        // Verify no content dropped
        Assert.Equal(expectedText.Length, finalContent.Length);
        Assert.Equal(expectedText, finalContent);

        Log("TEST PASSED");
    }

    /// <summary>
    /// Multiple rounds to catch intermittent race conditions.
    /// </summary>
    [Theory]
    [InlineData(20)]
    public async Task StreamReader_ContentNeverDropped_MultiRound(int rounds)
    {
        Log($"TEST START: StreamReader_ContentNeverDropped_MultiRound ({rounds} rounds)");

        for (int round = 0; round < rounds; round++)
        {
            _messenger.Reset();

            // Generate unique content for each round
            var content = $"Round{round:D2}:" + new string('X', 500 + round * 10);
            _aiAgentFactory.ConfigureResponse(content);

            // Must add messages first before requesting response
            var messages = new List<ChatMessage>
            {
                new([ChatMessage.CreateText($"Question {round}")], MessageRole.eRoleUser, "testuser")
            };
            await _stateMachine.FireAsync(ChatTrigger.UserAddMessages, new AddMessagesContext(messages), default);
            await _stateMachine.FireAsync(ChatTrigger.UserRequestResponse,
                new CancellableContext(CancellationToken.None), default);

            var finalContent = _messenger.GetFinalTextContent();

            if (finalContent != content)
            {
                Log($"RACE DETECTED at round {round}!");
                Log($"Expected length: {content.Length}, Actual length: {finalContent.Length}");
                if (finalContent.Length < content.Length)
                {
                    Log($"Missing {content.Length - finalContent.Length} characters");
                }
                Assert.Fail($"Content mismatch at round {round}");
            }
        }

        Log("TEST PASSED");
    }

    [Fact]
    public async Task Streaming_TextIntegrity_ExactStepBoundary_NoDataLoss()
    {
        Log("TEST START: Streaming_TextIntegrity_ExactStepBoundary_NoDataLoss");

        // Create content where first chunk is exactly the UI update step (168 chars).
        var firstPart = new string('A', 168);
        var secondPart = new string('B', 200);
        var fullContent = firstPart + secondPart;

        _aiAgentFactory.ConfigureChunkedResponse([
            (firstPart, 0),
            (secondPart, 50)
        ]);

        await SendMessageAndRequestResponse();

        var finalContent = _messenger.GetAllTextContent();

        Assert.Equal(fullContent.Length, finalContent.Length);
        Assert.Equal(fullContent, finalContent);

        Log("TEST PASSED");
    }

    [Fact]
    public async Task Streaming_TextIntegrity_Cyrillic_NoReplacementChar()
    {
        Log("TEST START: Streaming_TextIntegrity_Cyrillic_NoReplacementChar");

        var filler = new string('Ж', 333); // 333 * 2 bytes = 666 bytes in UTF-8
        var expectedText = filler + "AНа" + "личие END";

        _aiAgentFactory.ConfigureChunkedResponse([(expectedText, 0)]);

        await SendMessageAndRequestResponse();

        var finalContent = _messenger.GetAllTextContent();
        Assert.Equal(expectedText, finalContent);
        Assert.DoesNotContain("\uFFFD", finalContent);

        Log("TEST PASSED");
    }

    /// <summary>
    /// Tests content exactly at buffer boundary (168 chars * 4 bytes = 672 bytes).
    /// </summary>
    [Fact]
    public async Task StreamReader_ExactBufferBoundary_NoDataLoss()
    {
        Log("TEST START: StreamReader_ExactBufferBoundary_NoDataLoss");

        // 672 bytes is exactly the buffer size used in GetWrappedStream (168 * 4)
        var exactBoundaryContent = new string('X', 672);
        var afterBoundary = "AFTER_BOUNDARY";
        var fullContent = exactBoundaryContent + afterBoundary;

        _aiAgentFactory.ConfigureChunkedResponse([
            (exactBoundaryContent, 0),
            (afterBoundary, 0)
        ]);

        await SendMessageAndRequestResponse();

        var finalContent = _messenger.GetAllTextContent();
        Assert.Equal(fullContent.Length, finalContent.Length);
        Assert.Equal(fullContent, finalContent);

        Log("TEST PASSED");
    }

    /// <summary>
    /// Tests content larger than the initial buffer size.
    /// </summary>
    [Fact]
    public async Task StreamReader_LargerThanBuffer_NoDataLoss()
    {
        Log("TEST START: StreamReader_LargerThanBuffer_NoDataLoss");

        // Create content significantly larger than the 672 byte buffer
        var largeContent = new string('L', 2000);

        _aiAgentFactory.ConfigureResponse(largeContent);

        await SendMessageAndRequestResponse();

        var finalContent = _messenger.GetAllTextContent();
        Assert.Equal(largeContent.Length, finalContent.Length);
        Assert.Equal(largeContent, finalContent);

        Log("TEST PASSED");
    }

    /// <summary>
    /// Tests Unicode content (multi-byte UTF-8 characters).
    /// </summary>
    [Fact]
    public async Task StreamReader_UnicodeContent_NoDataLoss()
    {
        Log("TEST START: StreamReader_UnicodeContent_NoDataLoss");

        // Mix of ASCII, 2-byte, 3-byte, and 4-byte UTF-8 characters
        var unicodeContent = "Hello! Привет! 你好! こんにちは! 🎉🚀💻 Emojis and symbols: €£¥₹ Mathematical: ∑∏∫∂ Greek: αβγδ";
        _aiAgentFactory.ConfigureResponse(unicodeContent);

        await SendMessageAndRequestResponse();

        var finalContent = _messenger.GetFinalTextContent();
        Assert.Equal(unicodeContent, finalContent);

        Log("TEST PASSED");
    }

    /// <summary>
    /// Tests very large content (10KB) to ensure no data loss at scale.
    /// </summary>
    [Fact]
    public async Task StreamReader_VeryLargeContent_NoDataLoss()
    {
        Log("TEST START: StreamReader_VeryLargeContent_NoDataLoss");

        // 10KB of content
        var sb = new StringBuilder();
        for (int i = 0; i < 200; i++)
        {
            sb.Append($"Line {i:D4}: This is a test line with some content to make it longer. ");
        }
        var largeContent = sb.ToString();
        var expectedHash = ComputeSha256(largeContent);

        _aiAgentFactory.ConfigureResponse(largeContent);

        await SendMessageAndRequestResponse();

        var finalContent = _messenger.GetAllTextContent();
        var actualHash = ComputeSha256(finalContent);

        Assert.Equal(expectedHash, actualHash);
        Assert.Equal(largeContent.Length, finalContent.Length);

        Log("TEST PASSED");
    }

    /// <summary>
    /// Tests multiple buffer boundary crossings in chunked response.
    /// </summary>
    [Theory]
    [InlineData(100, 200, 300)]  // Small chunks
    [InlineData(672, 500, 672)]  // Exact buffer boundaries
    [InlineData(1000, 1000, 1000)]  // Large equal chunks
    public async Task StreamReader_MultipleChunks_NoDataLoss(int chunk1Size, int chunk2Size, int chunk3Size)
    {
        Log($"TEST START: StreamReader_MultipleChunks_NoDataLoss ({chunk1Size}, {chunk2Size}, {chunk3Size})");

        var chunk1 = new string('A', chunk1Size);
        var chunk2 = new string('B', chunk2Size);
        var chunk3 = new string('C', chunk3Size);
        var fullContent = chunk1 + chunk2 + chunk3;

        _aiAgentFactory.ConfigureChunkedResponse([
            (chunk1, 10),
            (chunk2, 10),
            (chunk3, 10)
        ]);

        await SendMessageAndRequestResponse();

        var finalContent = _messenger.GetAllTextContent();
        Assert.Equal(fullContent.Length, finalContent.Length);
        Assert.Equal(fullContent, finalContent);

        Log("TEST PASSED");
    }

    /// <summary>
    /// Repeats the stress test multiple times to catch any intermittent issues.
    /// </summary>
    [Fact]
    public async Task StreamReader_StressTest_RepeatedRuns()
    {
        Log("TEST START: StreamReader_StressTest_RepeatedRuns (1000 runs)");

        for (int run = 0; run < 1000; run++)
        {
            _messenger.Reset();

            // Generate different content for each run with varied sizes
            var contentSize = 100 + (run % 500);  // Varies from 100 to 599 chars
            var content = $"Run{run:D4}:" + new string((char)('A' + (run % 26)), contentSize);
            _aiAgentFactory.ConfigureResponse(content);

            var messages = new List<ChatMessage>
            {
                new([ChatMessage.CreateText($"Test {run}")], MessageRole.eRoleUser, "testuser")
            };
            await _stateMachine.FireAsync(ChatTrigger.UserAddMessages, new AddMessagesContext(messages), default);
            await _stateMachine.FireAsync(ChatTrigger.UserRequestResponse,
                new CancellableContext(CancellationToken.None), default);

            var finalContent = _messenger.GetAllTextContent();

            if (finalContent != content)
            {
                Log($"FAILURE at run {run}: expected {content.Length} chars, got {finalContent.Length}");
                Assert.Fail($"Content mismatch at run {run}: expected '{content.Substring(0, Math.Min(50, content.Length))}...', got '{finalContent.Substring(0, Math.Min(50, finalContent.Length))}...'");
            }
        }

        Log("TEST PASSED: All 1000 runs completed successfully");
    }

    private static string ComputeSha256(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    #endregion

    #region Helper Methods

    private async Task SendMessageAndRequestResponse()
    {
        var messages = new List<ChatMessage>
        {
            new([ChatMessage.CreateText("Test message")], MessageRole.eRoleUser, "testuser")
        };
        await _stateMachine.FireAsync(ChatTrigger.UserAddMessages, new AddMessagesContext(messages), default);
        await _stateMachine.FireAsync(ChatTrigger.UserRequestResponse,
            new CancellableContext(CancellationToken.None), default);
    }

    #endregion

    #region Mock Classes

    private class DelayableAiAgentFactory : IAiAgentFactory
    {
        private string _simpleResponse = "Default response";
        private (string text, int delayMs)[]? _chunkedResponse;
        private DelayableAiAgent? _currentAgent;

        public void ConfigureResponse(string response)
        {
            _simpleResponse = response;
            _chunkedResponse = null;
            // Update the current agent if one exists
            _currentAgent?.ConfigureSimple(response);
        }

        public void ConfigureChunkedResponse((string text, int delayMs)[] chunks)
        {
            _chunkedResponse = chunks;
            // Update the current agent if one exists
            _currentAgent?.ConfigureChunked(chunks);
        }

        public IAiAgent CreateAiAgent(string aiName, string aiSettings, bool useTools, bool imageOnlyMode, bool useFlash)
        {
            var agent = new DelayableAiAgent(aiName);
            if (_chunkedResponse != null)
            {
                agent.ConfigureChunked(_chunkedResponse);
            }
            else
            {
                agent.ConfigureSimple(_simpleResponse);
            }
            _currentAgent = agent;
            return agent;
        }
    }

    private class DelayableAiAgent : IAiAgent
    {
        private string _simpleResponse = "";
        private (string text, int delayMs)[]? _chunkedResponse;

        public DelayableAiAgent(string aiName) => AiName = aiName;
        public string AiName { get; }

        public void ConfigureSimple(string response) => _simpleResponse = response;
        public void ConfigureChunked((string text, int delayMs)[] chunks) => _chunkedResponse = chunks;

        public Task<string> GetResponse(string userId, string setting, string question, string? data, CancellationToken cancellationToken = default)
            => Task.FromResult("Response");

        public Task<IAiStreamingResponse> GetResponseStreamAsync(string userId, IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            if (_chunkedResponse != null)
            {
                return Task.FromResult<IAiStreamingResponse>(new ChunkedDelayStreamingResponse(_chunkedResponse, cancellationToken));
            }
            return Task.FromResult<IAiStreamingResponse>(new SingleTextStreamingResponse(_simpleResponse));
        }

        public Task<ImageContentItem> GetImage(string imageDescription, string imageSize, string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImageContentItem());
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

    /// <summary>
    /// A response stream that yields text chunks with configurable delays between them.
    /// </summary>
    private sealed class ChunkedDelayStreamingResponse : IAiStreamingResponse
    {
        private readonly (string text, int delayMs)[] _chunks;
        private readonly CancellationToken _ct;

        public ChunkedDelayStreamingResponse((string text, int delayMs)[] chunks, CancellationToken ct)
        {
            _chunks = chunks;
            _ct = ct;
        }

        public IAsyncEnumerable<string> GetTextDeltasAsync(CancellationToken cancellationToken = default)
        {
            return Stream(cancellationToken);

            async IAsyncEnumerable<string> Stream([EnumeratorCancellation] CancellationToken cancellationToken)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_ct, cancellationToken);
                var ct = linkedCts.Token;

                for (int i = 0; i < _chunks.Length; i++)
                {
                    var delayMs = _chunks[i].delayMs;
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, ct).ConfigureAwait(false);
                    }

                    yield return _chunks[i].text;
                }
            }
        }

        public List<ContentItem>? GetStructuredContent() => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Messenger that tracks all updates for verification.
    /// Enhanced to detect race conditions via ordered update tracking.
    /// </summary>
    private class TrackingMessenger : IMessenger
    {
        private readonly ConcurrentBag<string> _sentMessages = new();
        private readonly ConcurrentDictionary<string, List<string>> _messageUpdates = new();
        private string? _lastMessageId;
        private int _nextMessageId = 1000; // Start from 1000 to make IDs distinguishable

        // Race condition detection: ordered tracking with sequence numbers
        private readonly object _orderLock = new();
        private readonly List<(int Seq, string MessageId, string Content, DateTime Timestamp)> _orderedUpdates = [];
        private int _updateSequence;

        public int UpdateCount => _messageUpdates.Values.Sum(v => v.Count);

        public void Reset()
        {
            _sentMessages.Clear();
            _messageUpdates.Clear();
            _lastMessageId = null;
            _nextMessageId = 1000;
            lock (_orderLock)
            {
                _orderedUpdates.Clear();
                _updateSequence = 0;
            }
        }

        public string GetFinalTextContent()
        {
            if (_lastMessageId == null) return "";
            if (_messageUpdates.TryGetValue(_lastMessageId, out var updates) && updates.Count > 0)
            {
                return updates[^1]; // Last update
            }
            return "";
        }

        public string GetAllTextContent()
        {
            // Concatenate all messages' final content
            var sb = new StringBuilder();
            foreach (var kvp in _messageUpdates.OrderBy(k => k.Key))
            {
                if (kvp.Value.Count > 0)
                {
                    sb.Append(kvp.Value[^1]);
                }
            }
            return sb.ToString();
        }

        public List<string> GetIntermediateUpdates()
        {
            return _messageUpdates.Values.SelectMany(v => v).ToList();
        }

        /// <summary>
        /// Gets all updates in the order they were received, for race condition detection.
        /// </summary>
        public List<(int Seq, string MessageId, string Content)> GetOrderedUpdates()
        {
            lock (_orderLock)
            {
                return _orderedUpdates.Select(u => (u.Seq, u.MessageId, u.Content)).ToList();
            }
        }

        /// <summary>
        /// Gets updates for a specific message in order received.
        /// </summary>
        public List<string> GetOrderedUpdatesForMessage(string messageId)
        {
            lock (_orderLock)
            {
                return _orderedUpdates
                    .Where(u => u.MessageId == messageId)
                    .OrderBy(u => u.Seq)
                    .Select(u => u.Content)
                    .ToList();
            }
        }

        public int MaxTextMessageLen() => 4096;
        public int MaxPhotoMessageLen() => 1024;

        public Task<bool> DeleteMessage(string chatId, MessageId messageId) => Task.FromResult(true);

        public Task<string> SendTextMessage(string chatId, MessengerMessageDTO message, IEnumerable<ActionId>? messageActionIds = null)
        {
            var id = (_nextMessageId++).ToString();
            _lastMessageId = id;
            _sentMessages.Add(message.TextContent ?? "");
            _messageUpdates[id] = [message.TextContent ?? ""];

            lock (_orderLock)
            {
                _orderedUpdates.Add((_updateSequence++, id, message.TextContent ?? "", DateTime.UtcNow));
            }

            return Task.FromResult(id);
        }

        public Task EditTextMessage(string chatId, MessageId messageId, string content, IEnumerable<ActionId>? messageActionIds = null)
        {
            var id = messageId.Value ?? "";
            if (!_messageUpdates.ContainsKey(id))
            {
                _messageUpdates[id] = [];
            }
            _messageUpdates[id].Add(content);

            lock (_orderLock)
            {
                _orderedUpdates.Add((_updateSequence++, id, content, DateTime.UtcNow));
            }

            return Task.CompletedTask;
        }

        public Task<string> SendPhotoMessage(string chatId, MessengerMessageDTO message, IEnumerable<ActionId>? messageActionIds = null)
            => Task.FromResult((_nextMessageId++).ToString());

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
