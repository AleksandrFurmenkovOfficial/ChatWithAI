using Stateless;

namespace ChatWithAI.Core.StateMachine
{
    public class ChatStateMachine : IDisposable
    {
        private bool _disposed;

        private readonly StateMachine<ChatState, ChatTrigger> _machine;
        private readonly IChatInternal _chat;
        private readonly ILogger? _logger;
        private readonly SemaphoreSlim _transitionLock = new(1, 1);
        private readonly Queue<Func<Task>> _pendingTriggers = new();

        // Parameterized triggers
        private readonly StateMachine<ChatState, ChatTrigger>.TriggerWithParameters<AddMessagesContext> _addMessagesTrigger;
        private readonly StateMachine<ChatState, ChatTrigger>.TriggerWithParameters<SetModeContext> _setModeTrigger;
        private readonly StateMachine<ChatState, ChatTrigger>.TriggerWithParameters<CancellableContext> _requestAnswerTrigger;
        private readonly StateMachine<ChatState, ChatTrigger>.TriggerWithParameters<CancellableContext> _continueTrigger;
        private readonly StateMachine<ChatState, ChatTrigger>.TriggerWithParameters<CancellableContext> _regenerateTrigger;
        private readonly StateMachine<ChatState, ChatTrigger>.TriggerWithParameters<StreamingContext> _aiProducedContentTrigger;

        public ChatStateMachine(IChatInternal chat, ILogger? logger = null)
        {
            _chat = chat ?? throw new ArgumentNullException(nameof(chat));
            _logger = logger;

            _machine = new StateMachine<ChatState, ChatTrigger>(ChatState.WaitingForFirstMessage);

            _addMessagesTrigger = _machine.SetTriggerParameters<AddMessagesContext>(ChatTrigger.UserAddMessages);
            _setModeTrigger = _machine.SetTriggerParameters<SetModeContext>(ChatTrigger.UserSetMode);
            _requestAnswerTrigger = _machine.SetTriggerParameters<CancellableContext>(ChatTrigger.UserRequestResponse);
            _continueTrigger = _machine.SetTriggerParameters<CancellableContext>(ChatTrigger.UserContinue);
            _regenerateTrigger = _machine.SetTriggerParameters<CancellableContext>(ChatTrigger.UserRegenerate);
            _aiProducedContentTrigger = _machine.SetTriggerParameters<StreamingContext>(ChatTrigger.AIProducedContent);

            ConfigureTransitions();

            _machine.OnTransitionCompletedAsync(OnTransitionCompletedAsync);

            _logger?.LogDebugMessage($"Chat {_chat.Id}: StateMachine initialized");
        }

        private void ConfigureTransitions()
        {
            // 1. WaitingForFirstMessage
            _machine.Configure(ChatState.WaitingForFirstMessage)
                .OnEntryAsync(OnWaitingForFirstMessageEnterAsync)
                .OnExit(() => _logger?.LogDebugMessage($"Chat {_chat.Id}: ← Exiting state WaitingForFirstMessage"))
                .Permit(ChatTrigger.UserAddMessages, ChatState.WaitingForNewMessages)
                .InternalTransitionAsync(_setModeTrigger, (ctx, _) => _chat.SetModeAsync(ctx.Mode))
                .PermitReentry(ChatTrigger.UserReset);

            // 2. WaitingForNewMessages
            _machine.Configure(ChatState.WaitingForNewMessages)
                .OnEntry(() => _logger?.LogDebugMessage($"Chat {_chat.Id}: → Entered state WaitingForNewMessages"))
                .OnExit(() => _logger?.LogDebugMessage($"Chat {_chat.Id}: ← Exiting state WaitingForNewMessages"))
                .OnEntryFromAsync(_addMessagesTrigger, (ctx, _) => _chat.AddUserMessagesToChatHistoryAsync(ctx.Messages))
                .InternalTransitionAsync(_addMessagesTrigger, (ctx, _) => _chat.AddUserMessagesToChatHistoryAsync(ctx.Messages))
                .InternalTransitionAsync(_setModeTrigger, (ctx, _) => _chat.SetModeAsync(ctx.Mode))
                .Permit(ChatTrigger.UserReset, ChatState.WaitingForFirstMessage)
                .Permit(ChatTrigger.UserRegenerate, ChatState.InitiateAIResponse)
                .Permit(ChatTrigger.UserContinue, ChatState.InitiateAIResponse)
                .Permit(ChatTrigger.UserRequestResponse, ChatState.InitiateAIResponse);

            // 3. InitiateAIResponse
            _machine.Configure(ChatState.InitiateAIResponse)
                .OnEntry(() => _logger?.LogDebugMessage($"Chat {_chat.Id}: → Entered state InitiateAIResponse"))
                .OnExit(() => _logger?.LogDebugMessage($"Chat {_chat.Id}: ← Exiting state InitiateAIResponse"))
                .OnEntryFromAsync(_requestAnswerTrigger, (ctx, _) => OnInitiateResponseAsync(ctx.CancellationToken))
                .OnEntryFromAsync(_continueTrigger, (ctx, _) => OnContinueResponseAsync(ctx.CancellationToken))
                .OnEntryFromAsync(_regenerateTrigger, (ctx, _) => OnRegenerateResponseAsync(ctx.CancellationToken))
                .OnEntryFromAsync(_setModeTrigger, (ctx, _) => _chat.SetModeAsync(ctx.Mode))
                .InternalTransitionAsync(_setModeTrigger, (ctx, _) => _chat.SetModeAsync(ctx.Mode))
                .Permit(ChatTrigger.UserAddMessages, ChatState.WaitingForNewMessages)
                .Permit(ChatTrigger.UserReset, ChatState.WaitingForFirstMessage)
                .Permit(ChatTrigger.UserCancel, ChatState.WaitingForNewMessages)
                .Permit(ChatTrigger.AIProducedContent, ChatState.Streaming)
                .Permit(ChatTrigger.AIResponseError, ChatState.Error);

            // 4. Streaming
            _machine.Configure(ChatState.Streaming)
                .OnEntry(() => _logger?.LogDebugMessage($"Chat {_chat.Id}: → Entered state Streaming"))
                .OnExit(() => _logger?.LogDebugMessage($"Chat {_chat.Id}: ← Exiting state Streaming"))
                .OnEntryFromAsync(_aiProducedContentTrigger, OnStreamingEntryAsync)
                .Permit(ChatTrigger.UserAddMessages, ChatState.WaitingForNewMessages)
                .Permit(ChatTrigger.UserSetMode, ChatState.InitiateAIResponse)
                .Permit(ChatTrigger.UserReset, ChatState.WaitingForFirstMessage)
                .Permit(ChatTrigger.UserStop, ChatState.WaitingForNewMessages)
                .Permit(ChatTrigger.AIResponseFinished, ChatState.WaitingForNewMessages)
                .Permit(ChatTrigger.AIResponseError, ChatState.Error);

            // 5. Error
            _machine.Configure(ChatState.Error)
                .OnEntryAsync(async () =>
                {
                    _logger?.LogDebugMessage($"Chat {_chat.Id}: → Entered state Error");
                    await _chat.OnEnterErrorAsync().ConfigureAwait(false);
                })
                .OnExitAsync(async () =>
                {
                    _logger?.LogDebugMessage($"Chat {_chat.Id}: ← Exiting state Error");
                    await _chat.OnExitErrorAsync().ConfigureAwait(false);
                })
                .Permit(ChatTrigger.UserRegenerate, ChatState.InitiateAIResponse)
                .Permit(ChatTrigger.UserAddMessages, ChatState.WaitingForNewMessages)
                .InternalTransitionAsync(_setModeTrigger, (ctx, _) => _chat.SetModeAsync(ctx.Mode))
                .Permit(ChatTrigger.UserReset, ChatState.WaitingForFirstMessage);
        }

        // === STATE TRANSITION HANDLERS ===

        private async Task OnWaitingForFirstMessageEnterAsync()
        {
            _logger?.LogDebugMessage($"Chat {_chat.Id}: → Entered state WaitingForFirstMessage");
            try
            {
                await _chat.OnEnterWaitingForFirstMessageAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { LogAndSwallow(ex); }
        }

        private async Task OnInitiateResponseAsync(CancellationToken ct)
        {
            _logger?.LogDebugMessage($"Chat {_chat.Id}: OnInitiateResponseAsync");
            try
            {
                var result = await _chat.InitiateResponseAsync(ct).ConfigureAwait(false);
                HandleOperationResult(result);
            }
            catch (Exception ex) { LogAndSwallow(ex); }
        }

        private async Task OnContinueResponseAsync(CancellationToken ct)
        {
            _logger?.LogDebugMessage($"Chat {_chat.Id}: OnContinueResponseAsync");
            try
            {
                var result = await _chat.ContinueResponseAsync(ct).ConfigureAwait(false);
                HandleOperationResult(result);
            }
            catch (Exception ex) { LogAndSwallow(ex); }
        }

        private async Task OnRegenerateResponseAsync(CancellationToken ct)
        {
            _logger?.LogDebugMessage($"Chat {_chat.Id}: OnRegenerateResponseAsync");
            try
            {
                var result = await _chat.RegenerateResponseAsync(ct).ConfigureAwait(false);
                HandleOperationResult(result);
            }
            catch (Exception ex) { LogAndSwallow(ex); }
        }

        private async Task OnStreamingEntryAsync(StreamingContext context, StateMachine<ChatState, ChatTrigger>.Transition transition)
        {
            _logger?.LogDebugMessage($"Chat {_chat.Id}: OnStreamingEntryAsync from {transition.Source}");
            try
            {
                var nextTrigger = await _chat.ProcessResponseStreamAsync(context.ResponseStream, context.CancellationToken).ConfigureAwait(false);
                EnqueueTrigger(nextTrigger);
            }
            catch (Exception ex)
            {
                LogAndSwallow(ex);
                EnqueueTrigger(ChatTrigger.AIResponseError);
            }
        }

        private void HandleOperationResult(ChatOperationResult result)
        {
            if (result.IsSuccess)
            {
                EnqueueTrigger(ChatTrigger.AIProducedContent, result.StreamingContext!);
            }
            else if (result.NextTrigger.HasValue)
            {
                EnqueueTrigger(result.NextTrigger.Value);
            }
        }

        private Task OnTransitionCompletedAsync(StateMachine<ChatState, ChatTrigger>.Transition transition)
        {
            _logger?.LogDebugMessage($"Chat {_chat.Id}: Transition completed: {transition.Source} -> {transition.Destination} via {transition.Trigger}");
            return Task.CompletedTask;
        }

        // === HELPERS ===

        private void LogAndSwallow(Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                _logger?.LogDebugMessage($"Chat {_chat.Id}: Operation was cancelled");
            }
            else
            {
                _logger?.LogErrorMessage($"Chat {_chat.Id}: Error in state machine action: {ex.Message}");
                _logger?.LogException(ex);
            }
        }

        /// <summary>
        /// Enqueues a trigger to be fired after the current transition completes.
        /// </summary>
        public void EnqueueTrigger(ChatTrigger trigger)
        {
            _logger?.LogDebugMessage($"Chat {_chat.Id}: Enqueuing trigger {trigger}");
            _pendingTriggers.Enqueue(() => _machine.FireAsync(trigger));
        }

        /// <summary>
        /// Enqueues a parameterized trigger to be fired after the current transition completes.
        /// </summary>
        public void EnqueueTrigger<TContext>(ChatTrigger trigger, TContext context) where TContext : TriggerContext
        {
            _logger?.LogDebugMessage($"Chat {_chat.Id}: Enqueuing trigger {trigger} with context");
            _pendingTriggers.Enqueue(() => FireContextAsync(trigger, context));
        }

        private async Task ProcessPendingTriggersAsync()
        {
            while (_pendingTriggers.TryDequeue(out var pendingTrigger))
            {
                _logger?.LogDebugMessage($"Chat {_chat.Id}: Processing pending trigger");
                await pendingTrigger().ConfigureAwait(false);
            }
        }

        // === PUBLIC API ===

        /// <summary>
        /// Returns the current state of the state machine.
        /// </summary>
        public ChatState CurrentState => _machine.State;

        /// <summary>
        /// Checks if a trigger can be fired in the current state.
        /// </summary>
        public bool CanFire(ChatTrigger trigger) => _machine.CanFire(trigger);

        /// <summary>
        /// Tries to fire a trigger without context. Returns false if the trigger is not permitted in the current state.
        /// </summary>
        public Task<bool> TryFireAsync(ChatTrigger trigger, CancellationToken cancellationToken)
        {
            return TryFireAsync(trigger, VoidContext.Instance, cancellationToken);
        }

        /// <summary>
        /// Tries to fire a trigger with context. Returns false if the trigger is not permitted in the current state.
        /// </summary>
        public async Task<bool> TryFireAsync<TContext>(ChatTrigger trigger, TContext context, CancellationToken cancellationToken) where TContext : TriggerContext
        {
            ArgumentNullException.ThrowIfNull(context);
            ThrowIfDisposed();

            var lockToken = GetCancellationToken(context, cancellationToken);

            try
            {
                await _transitionLock.WaitAsync(lockToken).ConfigureAwait(false);
                try
                {
                    if (!_machine.CanFire(trigger))
                    {
                        _logger?.LogDebugMessage($"Chat {_chat.Id}: Trigger {trigger} ignored - not permitted in state {_machine.State}");
                        return false;
                    }

                    _logger?.LogDebugMessage($"Chat {_chat.Id}: Firing trigger {trigger}");
                    await FireContextAsync(trigger, context).ConfigureAwait(false);
                    await ProcessPendingTriggersAsync().ConfigureAwait(false);
                    return true;
                }
                finally
                {
                    _transitionLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorMessage($"Chat {_chat.Id}: Failed to fire trigger {trigger}: {ex.Message}");
                _logger?.LogException(ex);
                throw;
            }
        }

        /// <summary>
        /// Fires a trigger with context.
        /// </summary>
        public async Task FireAsync<TContext>(ChatTrigger trigger, TContext context, CancellationToken cancellationToken) where TContext : TriggerContext
        {
            ArgumentNullException.ThrowIfNull(context);
            ThrowIfDisposed();

            _logger?.LogDebugMessage($"Chat {_chat.Id}: Firing trigger {trigger}");

            var lockToken = GetCancellationToken(context, cancellationToken);

            try
            {
                await _transitionLock.WaitAsync(lockToken).ConfigureAwait(false);
                try
                {
                    await FireContextAsync(trigger, context).ConfigureAwait(false);
                    await ProcessPendingTriggersAsync().ConfigureAwait(false);
                }
                finally
                {
                    _transitionLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorMessage($"Chat {_chat.Id}: Failed to fire trigger {trigger}: {ex.Message}");
                _logger?.LogException(ex);
                throw;
            }
        }

        /// <summary>
        /// Extracts CancellationToken from context if available, otherwise uses the provided token.
        /// </summary>
        private static CancellationToken GetCancellationToken<TContext>(TContext context, CancellationToken fallback) where TContext : TriggerContext
        {
            return context switch
            {
                CancellableContext c => c.CancellationToken,
                StreamingContext s => s.CancellationToken,
                _ => fallback
            };
        }

        private Task FireContextAsync<TContext>(ChatTrigger trigger, TContext context) where TContext : TriggerContext
        {
            return (trigger, context) switch
            {
                (ChatTrigger.UserAddMessages, AddMessagesContext addCtx) =>
                    _machine.FireAsync(_addMessagesTrigger, addCtx),

                (ChatTrigger.UserSetMode, SetModeContext modeCtx) =>
                    _machine.FireAsync(_setModeTrigger, modeCtx),

                (ChatTrigger.UserRequestResponse, CancellableContext cancellableCtx) =>
                    _machine.FireAsync(_requestAnswerTrigger, cancellableCtx),

                (ChatTrigger.UserContinue, CancellableContext cancellableCtx) =>
                    _machine.FireAsync(_continueTrigger, cancellableCtx),

                (ChatTrigger.UserRegenerate, CancellableContext cancellableCtx) =>
                    _machine.FireAsync(_regenerateTrigger, cancellableCtx),

                (ChatTrigger.AIProducedContent, StreamingContext streamCtx) =>
                    _machine.FireAsync(_aiProducedContentTrigger, streamCtx),

                (_, VoidContext) =>
                    _machine.FireAsync(trigger),

                _ => throw new InvalidOperationException(
                    $"Unknown trigger/context combination: {trigger} with {context.GetType().Name}")
            };
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _pendingTriggers.Clear();
            _transitionLock.Dispose();

            _logger?.LogDebugMessage($"Chat {_chat.Id}: StateMachine disposed");
            GC.SuppressFinalize(this);
        }
    }
}
