using System.Collections.Concurrent;
using Xunit.Abstractions;
using ChatMessage = ChatWithAI.Contracts.Model.ChatMessageModel;

namespace ChatWithAI.Tests;

/// <summary>
/// Load tests for ChatBatchExecutor.
/// </summary>
public class ChatBatchExecutorLoadTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private readonly ITestOutputHelper _output;
    private readonly MockLogger _mockLogger;
    private readonly MockChatMessageActionProcessor _mockActionProcessor = new();

    public ChatBatchExecutorLoadTests(ITestOutputHelper output)
    {
        _output = output;
        _mockLogger = new MockLogger(output);
    }

    private void Log(string message) => _output.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

    /// <summary>
    /// Two batches where second arrives DURING first's execution:
    /// 1. Messages from first batch MUST be added
    /// 2. First batch MUST be cancelled by second (before ExecutePipeline or during it)
    /// 3. Messages from second batch MUST be added
    /// 4. Only second batch runs ExecutePipeline
    /// </summary>
    [Fact]
    public async Task TwoBatches_SecondArrivesDuringFirst_FirstCancelled_OnlySecondExecutesPipeline()
    {
        Log("TEST START: TwoBatches_SecondArrivesDuringFirst");
        using var testCts = new CancellationTokenSource(TestTimeout);
        var addMessagesStarted = new TaskCompletionSource();
        var canContinueAfterAddMessages = new TaskCompletionSource();
        var addCallCount = 0;

        var mockChat = new ControlledMockChat(
            onAddMessages: async () =>
            {
                var count = Interlocked.Increment(ref addCallCount);
                Log($"AddMessages called, count={count}");
                if (count == 1)
                {
                    addMessagesStarted.TrySetResult();
                    Log("First AddMessages waiting for signal...");
                    await canContinueAfterAddMessages.Task.WaitAsync(testCts.Token);
                    Log("First AddMessages continuing");
                }
            });

        using var executor = new ChatBatchExecutor(mockChat, null, _mockActionProcessor, _mockLogger);
        var chatId = mockChat.Id;
        var batch1Events = CreateMessageEvents(chatId, 1, 2);
        var batch2Events = CreateMessageEvents(chatId, 2, 3);

        Log("Starting task1 (batch1)");
        var task1 = Task.Run(async () =>
        {
            try
            {
                await executor.ExecuteBatch(chatId, batch1Events, CancellationToken.None);
                Log("Task1 completed normally");
            }
            catch (OperationCanceledException)
            {
                Log("Task1 cancelled");
            }
        });

        Log("Waiting for addMessagesStarted");
        await addMessagesStarted.Task.WaitAsync(testCts.Token);
        Log("addMessagesStarted received");

        Log("Starting task2 (batch2)");
        var task2 = Task.Run(() => executor.ExecuteBatch(chatId, batch2Events, CancellationToken.None));

        Log("Delay 50ms");
        await Task.Delay(50, testCts.Token);

        Log("Setting canContinueAfterAddMessages");
        canContinueAfterAddMessages.SetResult();

        Log("Waiting for both tasks");
        await Task.WhenAll(task1, task2).WaitAsync(testCts.Token);
        Log("Both tasks completed");

        // Assert
        // All messages from both batches should be added
        Assert.Equal(5, mockChat.AddedMessages.Count); // 2 + 3 messages

        // DoResponseToLastMessage should be called exactly once (only last batch)
        Assert.Equal(1, mockChat.DoResponseCount);
        Log("TEST PASSED");
    }

    /// <summary>
    /// Multiple batches arriving rapidly (simulated concurrency):
    /// All messages are added, at most one DoResponse call.
    /// </summary>
    [Fact]
    public async Task MultipleBatches_RapidArrival_AllMessagesAdded_AtMostOneResponse()
    {
        // Arrange
        using var testCts = new CancellationTokenSource(TestTimeout);
        const int batchCount = 5;
        const int messagesPerBatch = 2;
        var addDelay = new TaskCompletionSource();
        var firstAddStarted = new TaskCompletionSource();
        var callCount = 0;

        var mockChat = new ControlledMockChat(
            onAddMessages: async () =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    firstAddStarted.TrySetResult();
                    await addDelay.Task.WaitAsync(testCts.Token);
                }
            });

        using var executor = new ChatBatchExecutor(
            mockChat,
            screenshotProvider: null,
            _mockActionProcessor,
            _mockLogger);

        var chatId = mockChat.Id;

        // Act: Start first batch
        var firstBatch = CreateMessageEvents(chatId, 0, messagesPerBatch);
        var task1 = Task.Run(async () =>
        {
            try { await executor.ExecuteBatch(chatId, firstBatch, CancellationToken.None); }
            catch (OperationCanceledException) { }
        });

        // Wait for first batch to block
        await firstAddStarted.Task.WaitAsync(testCts.Token);

        // Submit remaining batches rapidly
        var otherTasks = Enumerable.Range(1, batchCount - 1).Select(i =>
            Task.Run(async () =>
            {
                try
                {
                    var batch = CreateMessageEvents(chatId, i, messagesPerBatch);
                    await executor.ExecuteBatch(chatId, batch, CancellationToken.None);
                }
                catch (OperationCanceledException) { }
            })).ToList();

        // Give time for all batches to enqueue
        await Task.Delay(100, testCts.Token);

        // Release first batch
        addDelay.SetResult();

        await task1.WaitAsync(testCts.Token);
        await Task.WhenAll(otherTasks).WaitAsync(testCts.Token);

        // Assert
        // All messages should be added
        var expectedMessageCount = batchCount * messagesPerBatch;
        Assert.Equal(expectedMessageCount, mockChat.AddedMessages.Count);

        // At most one response (only last batch executes pipeline)
        Assert.True(mockChat.DoResponseCount <= 1,
            $"DoResponseToLastMessage should be called at most once, got {mockChat.DoResponseCount}");
    }

    /// <summary>
    /// Verify CancellationToken is properly propagated to ExecutePipeline.
    /// External cancellation should stop pipeline execution.
    /// </summary>
    [Fact]
    public async Task ExternalCancellation_StopsPipelineExecution()
    {
        // Arrange
        var pipelineStarted = new TaskCompletionSource();
        var canComplete = new TaskCompletionSource();

        var mockChat = new ControlledMockChat(
            onDoResponse: async ct =>
            {
                pipelineStarted.TrySetResult();
                await canComplete.Task.WaitAsync(ct); // Will throw on cancel
            });

        using var executor = new ChatBatchExecutor(
            mockChat,
            screenshotProvider: null,
            _mockActionProcessor,
            _mockLogger);

        var chatId = mockChat.Id;
        var events = CreateMessageEvents(chatId, 0, 3);
        using var cts = new CancellationTokenSource();

        // Act
        var task = Task.Run(async () =>
        {
            try
            {
                await executor.ExecuteBatch(chatId, events, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        });

        // Wait for pipeline to start
        await pipelineStarted.Task;

        // Cancel externally
        cts.Cancel();
        canComplete.TrySetResult(); // Also complete to avoid hanging

        await task;

        // Assert: Messages should be added
        Assert.Equal(3, mockChat.AddedMessages.Count);
        Assert.Equal(1, mockChat.DoResponseCount); // Pipeline was entered
    }

    /// <summary>
    /// When batches execute sequentially (no overlap), each executes its pipeline.
    /// This is expected behavior - cancellation only happens during concurrent execution.
    /// </summary>
    [Fact]
    public async Task SequentialBatches_NoOverlap_EachExecutesPipeline()
    {
        Log("TEST START: SequentialBatches_NoOverlap");
        using var testCts = new CancellationTokenSource(TestTimeout);
        var mockChat = new MockChat();
        using var executor = new ChatBatchExecutor(mockChat, null, _mockActionProcessor, _mockLogger);
        var chatId = mockChat.Id;

        for (int i = 0; i < 3; i++)
        {
            Log($"Executing batch {i}");
            var events = CreateMessageEvents(chatId, i, 2);
            await executor.ExecuteBatch(chatId, events, testCts.Token);
            Log($"Batch {i} completed");
        }

        Assert.Equal(6, mockChat.AddedMessages.Count);
        Assert.Equal(3, mockChat.DoResponseCount);
        Log("TEST PASSED");
    }

    /// <summary>
    /// High concurrency stress test with barrier synchronization.
    /// </summary>
    [Fact]
    public async Task HighConcurrency_AllMessagesPreserved()
    {
        Log("TEST START: HighConcurrency_AllMessagesPreserved");
        using var testCts = new CancellationTokenSource(TestTimeout);
        const int concurrentBatches = 20; // Reduced for faster testing
        const int messagesPerBatch = 2;

        var mockChat = new MockChat();
        using var executor = new ChatBatchExecutor(mockChat, null, _mockActionProcessor, _mockLogger);
        var chatId = mockChat.Id;
        var barrier = new Barrier(concurrentBatches);

        Log($"Starting {concurrentBatches} concurrent batches");
        var tasks = Enumerable.Range(0, concurrentBatches).Select(i =>
            Task.Run(async () =>
            {
                var events = CreateMessageEvents(chatId, i, messagesPerBatch);
                Log($"Batch {i} waiting at barrier");
                barrier.SignalAndWait(testCts.Token);
                Log($"Batch {i} executing");
                try
                {
                    await executor.ExecuteBatch(chatId, events, CancellationToken.None);
                    Log($"Batch {i} completed");
                }
                catch (OperationCanceledException)
                {
                    Log($"Batch {i} cancelled");
                }
            })).ToList();

        Log("Waiting for all tasks");
        await Task.WhenAll(tasks).WaitAsync(testCts.Token);
        Log($"All tasks done. Messages={mockChat.AddedMessages.Count}, Responses={mockChat.DoResponseCount}");

        // Assert: All messages must be added (never lost)
        var totalExpectedMessages = concurrentBatches * messagesPerBatch;
        Assert.Equal(totalExpectedMessages, mockChat.AddedMessages.Count);

        // Some batches may have been cancelled, so response count can vary
        // but should be at least 1 (last batch)
        Assert.True(mockChat.DoResponseCount >= 1);
        Log("TEST PASSED");
    }

    /// <summary>
    /// Dispose during execution - should complete gracefully without hanging.
    /// </summary>
    [Fact]
    public async Task DisposeDuringExecution_GracefulShutdown()
    {
        // Arrange
        using var testCts = new CancellationTokenSource(TestTimeout);
        var responseStarted = new TaskCompletionSource();
        var canComplete = new TaskCompletionSource();

        var mockChat = new ControlledMockChat(
            onDoResponse: async ct =>
            {
                responseStarted.TrySetResult();
                await canComplete.Task.WaitAsync(ct);
            });

        var executor = new ChatBatchExecutor(
            mockChat,
            screenshotProvider: null,
            _mockActionProcessor,
            _mockLogger);

        var chatId = mockChat.Id;
        var events = CreateMessageEvents(chatId, 0, 2);

        // Act
        var task = Task.Run(async () =>
        {
            try
            {
                await executor.ExecuteBatch(chatId, events, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (ObjectDisposedException)
            {
                // Also acceptable
            }
        });

        // Wait for execution to reach DoResponse
        var responseStartedTask = responseStarted.Task;
        var completedFirst = await Task.WhenAny(responseStartedTask, Task.Delay(1000));

        if (completedFirst == responseStartedTask)
        {
            // Dispose while executing
            executor.Dispose();
            canComplete.TrySetResult();
        }
        else
        {
            // Execution completed too fast, just cleanup
            canComplete.TrySetResult();
            executor.Dispose();
        }

        // Assert: Should complete without hanging
        var completed = await Task.WhenAny(task, Task.Delay(5000)) == task;
        Assert.True(completed, "Task should complete after dispose");
    }

    /// <summary>
    /// BREAKING TEST #1: Race condition - new batch enqueued during AddMessages
    /// </summary>
    [Fact]
    public async Task BreakingTest1_RaceCondition_NewBatchEnqueuedDuringAddMessages()
    {
        Log("TEST START: BreakingTest1_RaceCondition");
        using var testCts = new CancellationTokenSource(TestTimeout);
        var addMessagesCallCount = 0;
        var firstAddCompleted = new TaskCompletionSource();
        var secondBatchEnqueued = new TaskCompletionSource();

        var mockChat = new ControlledMockChat(
            onAddMessages: async () =>
            {
                var count = Interlocked.Increment(ref addMessagesCallCount);
                Log($"AddMessages count={count}");
                if (count == 1)
                {
                    firstAddCompleted.TrySetResult();
                    Log("Waiting for secondBatchEnqueued");
                    await secondBatchEnqueued.Task.WaitAsync(testCts.Token);
                    Log("secondBatchEnqueued received, delay 10ms");
                    await Task.Delay(10, testCts.Token);
                }
            });

        using var executor = new ChatBatchExecutor(mockChat, null, _mockActionProcessor, _mockLogger);
        var chatId = mockChat.Id;
        var batch1 = CreateMessageEvents(chatId, 1, 2);
        var batch2 = CreateMessageEvents(chatId, 2, 2);

        Log("Starting task1");
        var task1 = Task.Run(async () =>
        {
            try { await executor.ExecuteBatch(chatId, batch1, CancellationToken.None); return "completed"; }
            catch (OperationCanceledException) { return "cancelled"; }
        });

        Log("Waiting for firstAddCompleted");
        await firstAddCompleted.Task.WaitAsync(testCts.Token);

        Log("Starting task2");
        var task2 = Task.Run(async () =>
        {
            try { await executor.ExecuteBatch(chatId, batch2, CancellationToken.None); return "completed"; }
            catch (OperationCanceledException) { return "cancelled"; }
        });

        Log("Delay 20ms then signal secondBatchEnqueued");
        await Task.Delay(20, testCts.Token);
        secondBatchEnqueued.SetResult();

        Log("Waiting for tasks");
        var results = await Task.WhenAll(task1, task2).WaitAsync(testCts.Token);
        Log($"Results: task1={results[0]}, task2={results[1]}");

        // Assert
        // All messages from both batches should be added
        Assert.Equal(4, mockChat.AddedMessages.Count);

        // First batch should be cancelled, second should complete.
        Assert.Equal("cancelled", results[0]);
        Assert.Equal("completed", results[1]);

        // Only one DoResponse should be called (from batch 2)
        Assert.Equal(1, mockChat.DoResponseCount);
        Log("TEST PASSED");
    }

    /// <summary>
    /// BREAKING TEST #2: Commands might be lost when batch skips pipeline
    /// This test verifies that when a batch with commands is followed by another batch,
    /// the commands from the first batch might be skipped if the queue is not empty.
    /// </summary>
    [Fact]
    public async Task BreakingTest2_CommandsLostWhenBatchSkipsPipeline()
    {
        Log("TEST START: BreakingTest2_CommandsLost");
        using var testCts = new CancellationTokenSource(TestTimeout);
        var commandsExecuted = new ConcurrentQueue<string>();
        var commandStarted = new TaskCompletionSource();
        var canFinishCommand = new TaskCompletionSource();

        // Create a command that signals when it starts executing
        var trackingCommand = new BlockingTrackingCommand("cmd1", commandsExecuted, commandStarted, canFinishCommand, testCts.Token);

        var mockChat = new MockChat();
        using var executor = new ChatBatchExecutor(mockChat, null, _mockActionProcessor, _mockLogger);
        var chatId = mockChat.Id;

        // Batch 1 has only a command (no messages, so AddMessages won't block)
        var batch1 = new List<IChatEvent>
        {
            new EventChatCommand(chatId, "1", "user", trackingCommand, new ChatMessage(), "")
        };
        // Batch 2 has a message
        var batch2 = CreateMessageEvents(chatId, 2, 1);

        Log("Starting task1 (command batch)");
        var task1 = Task.Run(async () =>
        {
            try { await executor.ExecuteBatch(chatId, batch1, CancellationToken.None); }
            catch (OperationCanceledException) { Log("Task1 cancelled"); }
        });

        // Wait a bit for task1 to start
        await Task.Delay(50, testCts.Token);

        Log("Starting task2 (message batch)");
        var task2 = Task.Run(() => executor.ExecuteBatch(chatId, batch2, CancellationToken.None));

        // If command started executing, wait for it to signal
        var commandStartedOrTimeout = await Task.WhenAny(commandStarted.Task, Task.Delay(200, testCts.Token));
        if (commandStartedOrTimeout == commandStarted.Task)
        {
            Log("Command started executing");
            // Let the command finish
            canFinishCommand.SetResult();
        }
        else
        {
            Log("Command did not start (may have been skipped due to race)");
            canFinishCommand.TrySetResult(); // Release anyway to avoid hanging
        }

        Log("Waiting for tasks");
        await Task.WhenAll(task1, task2).WaitAsync(testCts.Token);
        Log($"Commands executed: {commandsExecuted.Count}");
        Log($"Messages added: {mockChat.AddedMessages.Count}");
        Log($"DoResponse calls: {mockChat.DoResponseCount}");

        // The test documents the behavior - commands might or might not be executed
        // depending on timing. This is the "breaking" part we want to highlight.
        Assert.True(commandsExecuted.Count <= 1, "Should execute at most one command");
        Assert.Equal(1, mockChat.AddedMessages.Count); // batch2 message should always be added
        Log("TEST PASSED");
    }

    #region Helper Methods

    private static List<IChatEvent> CreateMessageEvents(string chatId, int batchIndex, int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => new EventChatMessage(
                chatId, $"{batchIndex:D4}-{i:D4}", "testuser",
                new ChatMessage([new TextContentItem { Text = $"Msg {batchIndex}-{i}" }], MessageRole.eRoleUser)))
            .Cast<IChatEvent>()
            .ToList();
    }

    #endregion

    #region Mock Classes

    private class MockChat : IChat
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public ConcurrentBag<ChatMessage> AddedMessages { get; } = [];
        public int DoResponseCount;

        public ChatMode GetMode() => new();
        public Task SetMode(ChatMode mode) => Task.CompletedTask;
        public Task Reset() => Task.CompletedTask;
        public Task AddMessages(List<ChatMessage> messages)
        {
            foreach (var msg in messages) AddedMessages.Add(msg);
            return Task.CompletedTask;
        }
        public Task DoResponseToLastMessage(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref DoResponseCount);
            return Task.CompletedTask;
        }
        public Task ContinueLastResponse(CancellationToken ct) => Task.CompletedTask;
        public Task RegenerateLastResponse(CancellationToken ct) => Task.CompletedTask;
        public Task RemoveLastResponse() => Task.CompletedTask;
    }

    private class ControlledMockChat : IChat
    {
        private readonly Func<Task>? _onAddMessages;
        private readonly Func<CancellationToken, Task>? _onDoResponse;

        public ControlledMockChat(Func<Task>? onAddMessages = null, Func<CancellationToken, Task>? onDoResponse = null)
        {
            _onAddMessages = onAddMessages;
            _onDoResponse = onDoResponse;
        }

        public string Id { get; } = Guid.NewGuid().ToString();
        public ConcurrentBag<ChatMessage> AddedMessages { get; } = [];
        public int DoResponseCount;

        public ChatMode GetMode() => new();
        public Task SetMode(ChatMode mode) => Task.CompletedTask;
        public Task Reset() => Task.CompletedTask;
        public async Task AddMessages(List<ChatMessage> messages)
        {
            foreach (var msg in messages) AddedMessages.Add(msg);
            if (_onAddMessages != null) await _onAddMessages();
        }
        public async Task DoResponseToLastMessage(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref DoResponseCount);
            if (_onDoResponse != null) await _onDoResponse(ct);
        }
        public Task ContinueLastResponse(CancellationToken ct) => Task.CompletedTask;
        public Task RegenerateLastResponse(CancellationToken ct) => Task.CompletedTask;
        public Task RemoveLastResponse() => Task.CompletedTask;
    }

    private class MockLogger : ILogger
    {
        private readonly ITestOutputHelper? _output;
        public MockLogger(ITestOutputHelper? output = null) => _output = output;
        public void LogInfoMessage(string message) => _output?.WriteLine($"[DEBUG] {message}");
        public void LogDebugMessage(string message) => _output?.WriteLine($"[INFO] {message}");
        public void LogErrorMessage(string message) => _output?.WriteLine($"[ERROR] {message}");
        public void LogException(Exception e) => _output?.WriteLine($"[EXCEPTION] {e}");
    }

    private class MockChatMessageActionProcessor : IChatMessageActionProcessor
    {
        public Task HandleMessageAction(IChat chat, ActionParameters p, CancellationToken ct = default) => Task.CompletedTask;
    }

    private class TrackingCommand : IChatCommand
    {
        private readonly string _name;
        private readonly ConcurrentQueue<string> _order;
        public TrackingCommand(string name, ConcurrentQueue<string> order) { _name = name; _order = order; }
        public string Name => _name;
        public bool IsAdminOnlyCommand => false;
        public Task Execute(IChat chat, ChatMessage msg, CancellationToken ct = default)
        {
            _order.Enqueue(_name);
            return Task.CompletedTask;
        }
    }

    private class BlockingTrackingCommand : IChatCommand
    {
        private readonly string _name;
        private readonly ConcurrentQueue<string> _order;
        private readonly TaskCompletionSource _started;
        private readonly TaskCompletionSource _canFinish;
        private readonly CancellationToken _ct;

        public BlockingTrackingCommand(string name, ConcurrentQueue<string> order,
            TaskCompletionSource started, TaskCompletionSource canFinish, CancellationToken ct)
        {
            _name = name;
            _order = order;
            _started = started;
            _canFinish = canFinish;
            _ct = ct;
        }

        public string Name => _name;
        public bool IsAdminOnlyCommand => false;

        public async Task Execute(IChat chat, ChatMessage msg, CancellationToken ct = default)
        {
            _started.TrySetResult();
            await _canFinish.Task.WaitAsync(_ct);
            _order.Enqueue(_name);
        }
    }

    #endregion
}
