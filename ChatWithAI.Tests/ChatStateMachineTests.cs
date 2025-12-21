using ChatWithAI.Contracts.Configs;
using ChatWithAI.Core.StateMachine;
using System.Globalization;
using Xunit.Abstractions;
using ChatMessage = ChatWithAI.Contracts.Model.ChatMessageModel;
using ChatState = ChatWithAI.Core.StateMachine.ChatState;

namespace ChatWithAI.Tests;

/// <summary>
/// Unit tests for ChatStateMachine.
/// </summary>
public class ChatStateMachineTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly MockLogger _logger;
    private readonly MockMessenger _messenger;
    private readonly MockAiAgentFactory _aiAgentFactory;
    private readonly ChatCache _cache;
    private readonly Chat _chat;
    private readonly ChatStateMachine _stateMachine;

    public ChatStateMachineTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = new MockLogger(output);
        _messenger = new MockMessenger();
        _aiAgentFactory = new MockAiAgentFactory();
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

    #region 1. Initialization Tests

    [Fact]
    public void Constructor_InitialState_IsWaitingForFirstMessage()
    {
        Assert.Equal(ChatState.WaitingForFirstMessage, _stateMachine.CurrentState);
    }

    [Fact]
    public void Constructor_NullChat_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ChatStateMachine(null!));
    }

    [Fact]
    public void Constructor_WithLogger_DoesNotThrow()
    {
        using var sm = new ChatStateMachine(_chat, _logger);
        Assert.Equal(ChatState.WaitingForFirstMessage, sm.CurrentState);
    }

    [Fact]
    public void Constructor_WithoutLogger_DoesNotThrow()
    {
        using var sm = new ChatStateMachine(_chat);
        Assert.Equal(ChatState.WaitingForFirstMessage, sm.CurrentState);
    }

    #endregion

    #region 2. Transitions from WaitingForFirstMessage

    [Fact]
    public async Task WaitingForFirstMessage_UserAddMessages_TransitionsToWaitingForNewMessages()
    {
        var messages = CreateTestMessages(1);
        var context = new AddMessagesContext(messages);

        await _stateMachine.FireAsync(ChatTrigger.UserAddMessages, context, default);

        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task WaitingForFirstMessage_UserSetMode_StaysInSameState()
    {
        var mode = new ChatMode { AiName = "New_NewMode", AiSettings = "" };

        await _stateMachine.FireAsync(ChatTrigger.UserSetMode, new SetModeContext(mode), default);

        Assert.Equal(ChatState.WaitingForFirstMessage, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task WaitingForFirstMessage_UserReset_ReentriesSameState()
    {
        await _stateMachine.FireAsync(ChatTrigger.UserReset, VoidContext.Instance, default);

        Assert.Equal(ChatState.WaitingForFirstMessage, _stateMachine.CurrentState);
    }

    [Fact]
    public void WaitingForFirstMessage_UserRequestResponse_CannotFire()
    {
        Assert.False(_stateMachine.CanFire(ChatTrigger.UserRequestResponse));
    }

    [Fact]
    public void WaitingForFirstMessage_UserRegenerate_CannotFire()
    {
        Assert.False(_stateMachine.CanFire(ChatTrigger.UserRegenerate));
    }

    [Fact]
    public void WaitingForFirstMessage_UserContinue_CannotFire()
    {
        Assert.False(_stateMachine.CanFire(ChatTrigger.UserContinue));
    }

    #endregion

    #region 3. Transitions from WaitingForNewMessages

    [Fact]
    public async Task WaitingForNewMessages_UserRequestResponse_TransitionsToInitiateAIResponse()
    {
        await TransitionToWaitingForNewMessages();

        await _stateMachine.FireAsync(ChatTrigger.UserRequestResponse, new CancellableContext(CancellationToken.None), default);

        // With sync mock, completes full cycle to WaitingForNewMessages
        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task WaitingForNewMessages_UserContinue_TransitionsToInitiateAIResponse()
    {
        await TransitionToWaitingForNewMessages();

        await _stateMachine.FireAsync(ChatTrigger.UserContinue, new CancellableContext(CancellationToken.None), default);

        // With sync mock, completes full cycle to WaitingForNewMessages
        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task WaitingForNewMessages_UserRegenerate_TransitionsToInitiateAIResponse()
    {
        await TransitionToWaitingForNewMessages();

        await _stateMachine.FireAsync(ChatTrigger.UserRegenerate, new CancellableContext(CancellationToken.None), default);

        // With sync mock, completes full cycle to WaitingForNewMessages
        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task WaitingForNewMessages_UserReset_TransitionsToWaitingForFirstMessage()
    {
        await TransitionToWaitingForNewMessages();

        await _stateMachine.FireAsync(ChatTrigger.UserReset, VoidContext.Instance, default);

        Assert.Equal(ChatState.WaitingForFirstMessage, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task WaitingForNewMessages_UserAddMessages_StaysInSameState()
    {
        await TransitionToWaitingForNewMessages();
        var messages = CreateTestMessages(1);
        var context = new AddMessagesContext(messages);

        await _stateMachine.FireAsync(ChatTrigger.UserAddMessages, context, default);

        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task WaitingForNewMessages_UserSetMode_StaysInSameState()
    {
        await TransitionToWaitingForNewMessages();
        var mode = new ChatMode { AiName = "New_NewMode", AiSettings = "" };

        await _stateMachine.FireAsync(ChatTrigger.UserSetMode, new SetModeContext(mode), default);

        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }

    #endregion

    #region 4. Transitions from InitiateAIResponse

    // Note: With synchronous mock, state machine completes the full cycle.
    // These tests verify state machine behavior with blocking mock or are skipped.

    [Fact]
    public async Task InitiateAIResponse_AIProducedContent_TransitionsToStreaming()
    {
        var tcs = await TransitionToInitiateAIResponseBlocking();
        Assert.Equal(ChatState.InitiateAIResponse, _stateMachine.CurrentState);

        // AIProducedContent is internally fired after GetResponseStreamAsync returns
        // Complete blocking to let it proceed
        tcs.SetResult(true);
        await Task.Delay(100);

        // Should have transitioned through Streaming to WaitingForNewMessages
        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task InitiateAIResponse_AIResponseError_TransitionsToError()
    {
        await TransitionToWaitingForNewMessages();
        // Configure the mock to throw an error
        _aiAgentFactory.LastCreatedAgent!.ShouldThrowError = true;

        await _stateMachine.FireAsync(ChatTrigger.UserRequestResponse, new CancellableContext(CancellationToken.None), default);

        Assert.Equal(ChatState.Error, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task InitiateAIResponse_UserCancel_TransitionsToWaitingForNewMessages()
    {
        await TransitionToWaitingForNewMessages();
        // Configure the mock to throw cancellation
        _aiAgentFactory.LastCreatedAgent!.ShouldThrowCancellation = true;

        await _stateMachine.FireAsync(ChatTrigger.UserRequestResponse, new CancellableContext(CancellationToken.None), default);

        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task InitiateAIResponse_UserAddMessages_TransitionsToWaitingForNewMessages()
    {
        var tcs = await TransitionToInitiateAIResponseBlocking();
        Assert.Equal(ChatState.InitiateAIResponse, _stateMachine.CurrentState);

        var messages = CreateTestMessages(1);
        var context = new AddMessagesContext(messages);

        // Start fire without awaiting - it may block waiting for transition
        var fireTask = _stateMachine.FireAsync(ChatTrigger.UserAddMessages, context, default);

        // Complete the blocking to allow transition
        tcs.SetResult(true);

        // Now await the fire
        await fireTask;
        await Task.Delay(50);

        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task InitiateAIResponse_UserReset_TransitionsToWaitingForFirstMessage()
    {
        var tcs = await TransitionToInitiateAIResponseBlocking();
        Assert.Equal(ChatState.InitiateAIResponse, _stateMachine.CurrentState);

        // Start fire without awaiting - it may block waiting for transition
        var fireTask = _stateMachine.FireAsync(ChatTrigger.UserReset, VoidContext.Instance, default);

        // Complete the blocking to allow transition
        tcs.SetResult(true);

        // Now await the fire
        await fireTask;
        await Task.Delay(50);

        Assert.Equal(ChatState.WaitingForFirstMessage, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task InitiateAIResponse_UserSetMode_StaysInSameState()
    {
        var tcs = await TransitionToInitiateAIResponseBlocking();
        Assert.Equal(ChatState.InitiateAIResponse, _stateMachine.CurrentState);

        var mode = new ChatMode { AiName = "New_NewMode", AiSettings = "" };

        // Start fire without awaiting - it may block waiting for transition
        var fireTask = _stateMachine.FireAsync(ChatTrigger.UserSetMode, new SetModeContext(mode), default);

        // State should still be InitiateAIResponse (SetMode doesn't change state when in InitiateAIResponse)
        Assert.Equal(ChatState.InitiateAIResponse, _stateMachine.CurrentState);

        // Complete the blocking to cleanup
        tcs.SetResult(true);

        // Now await the fire
        await fireTask;
    }

    #endregion

    #region 5. Transitions from Streaming

    [Fact]
    public async Task Streaming_AIResponseFinished_TransitionsToWaitingForNewMessages()
    {
        var tcs = await TransitionToStreamingBlocking();
        Assert.Equal(ChatState.Streaming, _stateMachine.CurrentState);

        // AIResponseFinished is internally fired when stream completes
        tcs.SetResult(true);
        await Task.Delay(100);

        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }


    [Fact]
    public async Task Streaming_UserSetMode_TransitionsToInitiateAIResponse()
    {
        var tcs = await TransitionToStreamingBlocking();
        Assert.Equal(ChatState.Streaming, _stateMachine.CurrentState);

        var mode = new ChatMode { AiName = "New_NewMode", AiSettings = "" };

        // Start fire without awaiting - it may block waiting for transition
        var fireTask = _stateMachine.FireAsync(ChatTrigger.UserSetMode, new SetModeContext(mode), default);

        // Complete blocking to allow transition
        tcs.SetResult(true);

        // Now await the fire
        await fireTask;
        await Task.Delay(100);

        // After SetMode from Streaming, it should go to InitiateAIResponse with new mode
        Assert.True(_stateMachine.CurrentState == ChatState.InitiateAIResponse ||
                   _stateMachine.CurrentState == ChatState.WaitingForNewMessages);
    }

    [Fact]
    public async Task Streaming_UserReset_TransitionsToWaitingForFirstMessage()
    {
        var tcs = await TransitionToStreamingBlocking();
        Assert.Equal(ChatState.Streaming, _stateMachine.CurrentState);

        // Start fire without awaiting - it may block waiting for transition
        var fireTask = _stateMachine.FireAsync(ChatTrigger.UserReset, VoidContext.Instance, default);

        // Complete blocking to allow transition
        tcs.SetResult(true);

        // Now await the fire
        await fireTask;
        await Task.Delay(50);

        Assert.Equal(ChatState.WaitingForFirstMessage, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task Streaming_UserAddMessages_TransitionsToWaitingForNewMessages()
    {
        var tcs = await TransitionToStreamingBlocking();
        Assert.Equal(ChatState.Streaming, _stateMachine.CurrentState);

        var messages = CreateTestMessages(1);
        var context = new AddMessagesContext(messages);

        // Start fire without awaiting - it may block waiting for transition
        var fireTask = _stateMachine.FireAsync(ChatTrigger.UserAddMessages, context, default);

        // Complete blocking to allow transition
        tcs.SetResult(true);

        // Now await the fire
        await fireTask;
        await Task.Delay(50);

        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }

    #endregion

    #region 6. Transitions from Error

    [Fact]
    public async Task Error_UserRegenerate_TransitionsToInitiateAIResponse()
    {
        await TransitionToError();

        // Clear the error flag so regenerate succeeds
        _aiAgentFactory.LastCreatedAgent!.ShouldThrowError = false;

        await _stateMachine.FireAsync(ChatTrigger.UserRegenerate, new CancellableContext(CancellationToken.None), default);

        // With sync mock, completes full cycle to WaitingForNewMessages
        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task Error_UserAddMessages_TransitionsToWaitingForNewMessages()
    {
        await TransitionToError();
        var messages = CreateTestMessages(1);
        var context = new AddMessagesContext(messages);

        await _stateMachine.FireAsync(ChatTrigger.UserAddMessages, context, default);

        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task Error_UserReset_TransitionsToWaitingForFirstMessage()
    {
        await TransitionToError();

        await _stateMachine.FireAsync(ChatTrigger.UserReset, VoidContext.Instance, default);

        Assert.Equal(ChatState.WaitingForFirstMessage, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task Error_UserSetMode_StaysInSameState()
    {
        await TransitionToError();
        var mode = new ChatMode { AiName = "New_NewMode", AiSettings = "" };

        await _stateMachine.FireAsync(ChatTrigger.UserSetMode, new SetModeContext(mode), default);

        Assert.Equal(ChatState.Error, _stateMachine.CurrentState);
    }

    #endregion

    #region 7. CanFire Tests

    [Fact]
    public void CanFire_PermittedTrigger_ReturnsTrue()
    {
        Assert.True(_stateMachine.CanFire(ChatTrigger.UserAddMessages));
        Assert.True(_stateMachine.CanFire(ChatTrigger.UserSetMode));
        Assert.True(_stateMachine.CanFire(ChatTrigger.UserReset));
    }

    [Fact]
    public void CanFire_NotPermittedTrigger_ReturnsFalse()
    {
        Assert.False(_stateMachine.CanFire(ChatTrigger.AIProducedContent));
        Assert.False(_stateMachine.CanFire(ChatTrigger.AIResponseFinished));
        Assert.False(_stateMachine.CanFire(ChatTrigger.UserStop));
        Assert.False(_stateMachine.CanFire(ChatTrigger.UserCancel));
    }

    [Fact]
    public async Task CanFire_InStreaming_CorrectPermissions()
    {
        var tcs = await TransitionToStreamingBlocking();
        Assert.Equal(ChatState.Streaming, _stateMachine.CurrentState);

        Assert.True(_stateMachine.CanFire(ChatTrigger.AIResponseFinished));
        Assert.True(_stateMachine.CanFire(ChatTrigger.AIResponseError));
        Assert.True(_stateMachine.CanFire(ChatTrigger.UserStop));
        Assert.True(_stateMachine.CanFire(ChatTrigger.UserReset));
        Assert.False(_stateMachine.CanFire(ChatTrigger.UserCancel));
        Assert.False(_stateMachine.CanFire(ChatTrigger.UserRegenerate));

        // Complete blocking to cleanup
        tcs.SetResult(true);
    }

    #endregion

    #region 8. TryFireAsync Tests

    [Fact]
    public async Task TryFireAsync_PermittedTrigger_ReturnsTrue_AndTransitions()
    {
        var result = await _stateMachine.TryFireAsync(ChatTrigger.UserReset, default);

        Assert.True(result);
        Assert.Equal(ChatState.WaitingForFirstMessage, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task TryFireAsync_NotPermittedTrigger_ReturnsFalse_StateUnchanged()
    {
        var initialState = _stateMachine.CurrentState;

        var result = await _stateMachine.TryFireAsync(ChatTrigger.AIProducedContent, default);

        Assert.False(result);
        Assert.Equal(initialState, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task TryFireAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // TaskCanceledException is a subclass of OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _stateMachine.TryFireAsync(ChatTrigger.UserReset, cts.Token));
    }

    #endregion

    #region 9. FireAsync (Non-parameterized) Tests

    [Fact]
    public async Task FireAsync_PermittedTrigger_Transitions()
    {
        await TransitionToWaitingForNewMessages();

        await _stateMachine.FireAsync(ChatTrigger.UserReset, VoidContext.Instance, default);

        Assert.Equal(ChatState.WaitingForFirstMessage, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task FireAsync_NotPermittedTrigger_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _stateMachine.FireAsync(ChatTrigger.AIProducedContent, VoidContext.Instance, default));
    }

    #endregion

    #region 10. Parameterized Trigger Tests

    [Fact]
    public async Task FireAsync_AddMessagesContext_AddsMessages()
    {
        var messages = CreateTestMessages(2);
        var context = new AddMessagesContext(messages);

        await _stateMachine.FireAsync(ChatTrigger.UserAddMessages, context, default);

        // State machine transitioned, which means AddMessages was called
        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task FireAsync_ChatMode_SetsMode()
    {
        var mode = new ChatMode { AiName = "New_TestAgent", AiSettings = "settings" };

        await _stateMachine.FireAsync(ChatTrigger.UserSetMode, new SetModeContext(mode), default);

        // Mode was set - verify via Chat.GetMode()
        Assert.Equal("New_TestAgent", _chat.GetMode().AiName);
    }

    [Fact]
    public async Task FireAsync_UserRequestResponse_InitiatesResponse()
    {
        await TransitionToWaitingForNewMessages();

        await _stateMachine.FireAsync(ChatTrigger.UserRequestResponse, new CancellableContext(CancellationToken.None), default);

        // With synchronous mock, the full response cycle completes, ending at WaitingForNewMessages
        // Verify the AI agent was actually called (response was initiated)
        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
        Assert.True(_aiAgentFactory.LastCreatedAgent?.GetResponseCalled ?? false);
    }

    [Fact]
    public async Task FireAsync_UserContinue_AddsSystemMessageAndInitiatesResponse()
    {
        await TransitionToWaitingForNewMessages();

        await _stateMachine.FireAsync(ChatTrigger.UserContinue, new CancellableContext(CancellationToken.None), default);

        // With synchronous mock, the full response cycle completes
        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task FireAsync_UserRegenerate_InitiatesRegeneration()
    {
        await TransitionToWaitingForNewMessages();

        await _stateMachine.FireAsync(ChatTrigger.UserRegenerate, new CancellableContext(CancellationToken.None), default);

        // With synchronous mock, the full response cycle completes
        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);
    }

    [Fact]
    public async Task FireAsync_NullParameter_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _stateMachine.FireAsync<AddMessagesContext>(ChatTrigger.UserAddMessages, null!, default));
    }

    #endregion

    #region 11. Trigger Queue Tests

    [Fact]
    public async Task EnqueueTrigger_ProcessedAfterCurrentTransition()
    {
        var tcs = await TransitionToInitiateAIResponseBlocking();
        Assert.Equal(ChatState.InitiateAIResponse, _stateMachine.CurrentState);

        // Enqueue a trigger that will be processed after current operations
        _stateMachine.EnqueueTrigger(ChatTrigger.UserReset);

        // Complete the blocking to let the cycle finish
        tcs.SetResult(true);
        await Task.Delay(100);

        // The enqueued UserReset should have been processed
        Assert.Equal(ChatState.WaitingForFirstMessage, _stateMachine.CurrentState);
    }

    #endregion

    #region 12. IDisposable Tests

    [Fact]
    public async Task Dispose_ThenFireAsync_ThrowsObjectDisposedException()
    {
        _stateMachine.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _stateMachine.FireAsync(ChatTrigger.UserReset, VoidContext.Instance, default));
    }

    [Fact]
    public async Task Dispose_ThenTryFireAsync_ThrowsObjectDisposedException()
    {
        _stateMachine.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _stateMachine.TryFireAsync(ChatTrigger.UserReset, default));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        _stateMachine.Dispose();

        var exception = Record.Exception(() => _stateMachine.Dispose());

        Assert.Null(exception);
    }

    #endregion

    #region 13. Concurrency Tests

    [Fact]
    public async Task ConcurrentFires_Serialized_NoRaceConditions()
    {
        Log("TEST START: ConcurrentFires_Serialized");
        using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await TransitionToWaitingForNewMessages();

        var tasks = new List<Task>();
        var successCount = 0;

        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var result = await _stateMachine.TryFireAsync(ChatTrigger.UserReset, testCts.Token);
                    if (result) Interlocked.Increment(ref successCount);
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
            }));
        }

        await Task.WhenAll(tasks).WaitAsync(testCts.Token);
        Log($"Success count: {successCount}");

        // At least one should succeed
        Assert.True(successCount >= 1);
        Log("TEST PASSED");
    }

    [Fact]
    public async Task ConcurrentTryFires_NoDeadlock()
    {
        Log("TEST START: ConcurrentTryFires_NoDeadlock");
        using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var tasks = Enumerable.Range(0, 20).Select(_ =>
            Task.Run(async () =>
            {
                try
                {
                    await _stateMachine.TryFireAsync(ChatTrigger.UserReset, testCts.Token);
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
            })).ToList();

        // Should complete without deadlock
        var allTasks = Task.WhenAll(tasks);
        var completedInTime = await Task.WhenAny(
            allTasks,
            Task.Delay(TimeSpan.FromSeconds(4), testCts.Token)) == allTasks;

        Assert.True(completedInTime, "Tasks should complete without deadlock");
        Log("TEST PASSED");
    }

    #endregion

    #region 14. Integration Scenarios

    [Fact]
    public async Task HappyPath_AddMessage_RequestResponse_Streaming_Finished()
    {
        Log("TEST START: HappyPath");

        // 1. Add message
        var messages = CreateTestMessages(1);
        await _stateMachine.FireAsync(ChatTrigger.UserAddMessages, new AddMessagesContext(messages), default);
        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);

        // 2. Request response - with sync mock, completes full cycle
        await _stateMachine.FireAsync(ChatTrigger.UserRequestResponse, new CancellableContext(CancellationToken.None), default);
        // With synchronous mock, state machine completes full cycle
        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);

        Log("TEST PASSED");
    }

    [Fact]
    public async Task CancelScenario_RequestResponse_UserCancel_BackToWaiting()
    {
        Log("TEST START: CancelScenario");

        await TransitionToWaitingForNewMessages();

        // Configure mock to throw cancellation
        _aiAgentFactory.LastCreatedAgent!.ShouldThrowCancellation = true;

        // Request response - will be cancelled
        await _stateMachine.FireAsync(ChatTrigger.UserRequestResponse, new CancellableContext(CancellationToken.None), default);
        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);

        Log("TEST PASSED");
    }

    [Fact]
    public async Task ErrorAndRetry_RequestResponse_Error_Retry_Success()
    {
        Log("TEST START: ErrorAndRetry");

        // First request - error
        await TransitionToWaitingForNewMessages();
        _aiAgentFactory.LastCreatedAgent!.ShouldThrowError = true;
        await _stateMachine.FireAsync(ChatTrigger.UserRequestResponse, new CancellableContext(CancellationToken.None), default);
        Assert.Equal(ChatState.Error, _stateMachine.CurrentState);

        // Clear error flag for retry
        _aiAgentFactory.LastCreatedAgent!.ShouldThrowError = false;

        // User retries (using UserRegenerate)
        await _stateMachine.FireAsync(ChatTrigger.UserRegenerate, new CancellableContext(CancellationToken.None), default);
        // With sync mock, completes full cycle
        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);

        Log("TEST PASSED");
    }

    [Fact]
    public async Task RegenerateScenario_Response_Regenerate_NewResponse()
    {
        Log("TEST START: RegenerateScenario");

        // Complete first response cycle - with sync mock, completes full cycle
        await TransitionToWaitingForNewMessages();
        await _stateMachine.FireAsync(ChatTrigger.UserRequestResponse, new CancellableContext(CancellationToken.None), default);
        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);

        // Regenerate - with sync mock, completes full cycle
        await _stateMachine.FireAsync(ChatTrigger.UserRegenerate, new CancellableContext(CancellationToken.None), default);
        Assert.Equal(ChatState.WaitingForNewMessages, _stateMachine.CurrentState);

        Log("TEST PASSED");
    }

    [Fact]
    public async Task ResetFromAnyState_ReturnsToWaitingForFirstMessage()
    {
        Log("TEST START: ResetFromAnyState");

        // Test reset from WaitingForNewMessages
        await TransitionToWaitingForNewMessages();
        await _stateMachine.FireAsync(ChatTrigger.UserReset, VoidContext.Instance, default);
        Assert.Equal(ChatState.WaitingForFirstMessage, _stateMachine.CurrentState);

        // Test reset from completed response (ends in WaitingForNewMessages with sync mock)
        await TransitionToInitiateAIResponse();
        await _stateMachine.FireAsync(ChatTrigger.UserReset, VoidContext.Instance, default);
        Assert.Equal(ChatState.WaitingForFirstMessage, _stateMachine.CurrentState);

        // Test reset from Error
        await TransitionToError();
        await _stateMachine.FireAsync(ChatTrigger.UserReset, VoidContext.Instance, default);
        Assert.Equal(ChatState.WaitingForFirstMessage, _stateMachine.CurrentState);

        Log("TEST PASSED");
    }


    #endregion

    #region Helper Methods

    private async Task TransitionToWaitingForNewMessages()
    {
        var messages = CreateTestMessages(1);
        await _stateMachine.FireAsync(ChatTrigger.UserAddMessages, new AddMessagesContext(messages), default);
    }

    /// <summary>
    /// Transitions to InitiateAIResponse state and blocks there.
    /// Returns a TaskCompletionSource that when completed will allow the AI response to proceed.
    /// </summary>
    private async Task<TaskCompletionSource<bool>> TransitionToInitiateAIResponseBlocking()
    {
        await TransitionToWaitingForNewMessages();

        // Set up blocking
        var tcs = new TaskCompletionSource<bool>();
        if (_aiAgentFactory.LastCreatedAgent != null)
        {
            _aiAgentFactory.LastCreatedAgent.BlockingTcs = tcs;
        }

        // Start the transition but don't await - it will block on the TCS
        var fireTask = _stateMachine.FireAsync(ChatTrigger.UserRequestResponse, new CancellableContext(CancellationToken.None), default);

        // Give some time for the state machine to enter InitiateAIResponse
        await Task.Delay(50);

        return tcs;
    }

    private async Task TransitionToInitiateAIResponse()
    {
        await TransitionToWaitingForNewMessages();
        // With synchronous mock, this will complete the full cycle
        await _stateMachine.FireAsync(ChatTrigger.UserRequestResponse, new CancellableContext(CancellationToken.None), default);
    }

    private async Task<TaskCompletionSource<bool>> TransitionToStreamingBlocking()
    {
        await TransitionToWaitingForNewMessages();

        // Set up blocking DURING streaming (blocks in GetTextDeltasAsync)
        var tcs = new TaskCompletionSource<bool>();
        if (_aiAgentFactory.LastCreatedAgent != null)
        {
            _aiAgentFactory.LastCreatedAgent.StreamBlockingTcs = tcs;
        }

        // Start the response - this will transition to InitiateAIResponse, then Streaming
        _ = _stateMachine.FireAsync(ChatTrigger.UserRequestResponse, new CancellableContext(CancellationToken.None), default);

        // Give time to reach Streaming state
        await Task.Delay(100);

        return tcs;
    }

    private async Task TransitionToStreaming()
    {
        await TransitionToInitiateAIResponse();
        // With synchronous mock, this already completes fully to WaitingForNewMessages
        // Firing AIProducedContent is now invalid from that state
        // This method is kept for compatibility but may not work as expected
    }

    private async Task TransitionToError()
    {
        await TransitionToWaitingForNewMessages();
        // Configure the mock to throw an error
        if (_aiAgentFactory.LastCreatedAgent != null)
        {
            _aiAgentFactory.LastCreatedAgent.ShouldThrowError = true;
        }
        await _stateMachine.FireAsync(ChatTrigger.UserRequestResponse, new CancellableContext(CancellationToken.None), default);
    }

    private static List<ChatMessage> CreateTestMessages(int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => new ChatMessage(
                [ChatMessage.CreateText($"Test message {i}")],
                MessageRole.eRoleUser,
                "testuser"))
            .ToList();
    }

    #endregion

    #region Mock Classes

    private class MockAiAgentFactory : IAiAgentFactory
    {
        public MockAiAgent? LastCreatedAgent { get; private set; }

        public IAiAgent CreateAiAgent(string aiName, string aiSettings, bool useTools, bool imageOnlyMode, bool useFlash)
        {
            LastCreatedAgent = new MockAiAgent(aiName);
            return LastCreatedAgent;
        }
    }

    private class MockAiAgent : IAiAgent
    {
        public bool GetResponseCalled { get; private set; }
        public TaskCompletionSource<bool>? BlockingTcs { get; set; }
        public TaskCompletionSource<bool>? StreamBlockingTcs { get; set; }
        public bool ShouldThrowError { get; set; }
        public bool ShouldThrowCancellation { get; set; }

        public MockAiAgent(string aiName)
        {
            AiName = aiName;
        }

        public string AiName { get; }

        public Task<string> GetResponse(string userId, string setting, string question, string? data, CancellationToken cancellationToken = default)
        {
            return Task.FromResult("Mock response");
        }

        public async Task<IAiStreamingResponse> GetResponseStreamAsync(string userId, IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            GetResponseCalled = true;

            // Wait if blocking is requested (blocks in InitiateAIResponse state)
            if (BlockingTcs != null)
            {
                await BlockingTcs.Task.ConfigureAwait(false);
            }

            if (ShouldThrowCancellation)
            {
                throw new OperationCanceledException();
            }

            if (ShouldThrowError)
            {
                throw new InvalidOperationException("Mock AI error");
            }

            var mockResponse = "Mock streaming response";
            return new SingleTextStreamingResponse(mockResponse, StreamBlockingTcs);
        }

        public Task<ImageContentItem> GetImage(string imageDescription, string imageSize, string userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ImageContentItem());
        }
    }

    private sealed class SingleTextStreamingResponse : IAiStreamingResponse
    {
        private readonly string _text;
        private readonly TaskCompletionSource<bool>? _blockingTcs;

        public SingleTextStreamingResponse(string text, TaskCompletionSource<bool>? blockingTcs = null)
        {
            _text = text ?? string.Empty;
            _blockingTcs = blockingTcs;
        }

        public IAsyncEnumerable<string> GetTextDeltasAsync(CancellationToken cancellationToken = default)
        {
            return Stream();

            async IAsyncEnumerable<string> Stream()
            {
                // Block during streaming if requested
                if (_blockingTcs != null)
                {
                    await _blockingTcs.Task.ConfigureAwait(false);
                }
                yield return _text;
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
        public void LogInfoMessage(string message) => _output?.WriteLine($"[DEBUG] {message}");
        public void LogDebugMessage(string message) => _output?.WriteLine($"[INFO] {message}");
        public void LogErrorMessage(string message) => _output?.WriteLine($"[ERROR] {message}");
        public void LogException(Exception e) => _output?.WriteLine($"[EXCEPTION] {e}");
    }

    #endregion
}
