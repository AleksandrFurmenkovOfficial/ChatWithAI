using ChatWithAI.Contracts.Model;
using System.Collections.Concurrent;

namespace ChatWithAI.Core
{
    /// <summary>
    /// Executes batched chat events in a specific order with cancellation support.
    /// Ensures messages are never lost even when operations are cancelled.
    /// </summary>
    public sealed class ChatBatchExecutor : IDisposable
    {
        private readonly IChat chat;
        private readonly ILogger logger;
        private readonly IScreenshotProvider? screenshotProvider;
        private readonly IChatMessageActionProcessor chatMessageActionProcessor;

        private readonly SemaphoreSlim executionLock = new(1, 1);
        private readonly ConcurrentQueue<EventBatch> batches = new();
        private bool disposed;

        // The CTS for the currently executing batch (if any).
        private CancellationTokenSource? currentExecutionCts;

        public ChatBatchExecutor(
            IChat chat,
            IScreenshotProvider? screenshotProvider,
            IChatMessageActionProcessor chatMessageActionProcessor,
            ILogger logger)
        {
            this.chat = chat ?? throw new ArgumentNullException(nameof(chat));
            this.screenshotProvider = screenshotProvider;
            this.chatMessageActionProcessor = chatMessageActionProcessor ?? throw new ArgumentNullException(nameof(chatMessageActionProcessor));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Executes a batch of events in the following order:
        /// 1. Expire → only if single event → Reset
        /// 2. CtrlC/CtrlV → Screenshot + prompt + DoResponse (each!)
        /// 3. Commands → all in sequence
        /// 4. Actions → only LAST, only if messages.Count == 0
        /// 5. Messages → all added, one DoResponse at the end
        ///
        /// Messages are never lost - they are added to chat even if cancelled.
        /// </summary>
        public async Task ExecuteBatch(
            string chatId,
            IEnumerable<IChatEvent> events,
            CancellationToken externalCt)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            var batch = ClassifyEvents(events);
            batches.Enqueue(batch); // store batch order
            await CancelCurrentTask().ConfigureAwait(false);
            await RunActualTask(chatId, externalCt).ConfigureAwait(false);
        }

        private async Task CancelCurrentTask()
        {
            var oldCts = Interlocked.Exchange(ref currentExecutionCts, null);
            if (oldCts != null)
            {
                try
                {
                    await oldCts.CancelAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // Previous CTS already disposed in "5. Clean up CTS and release lock"; ignore.
                }
                // Note: Do NOT dispose here - the token may still be in use by the running task.
                // The CTS will be disposed in the finally block of RunActualTask.
            }
        }

        private async Task RunActualTask(string chatId, CancellationToken externalCt)
        {
            CancellationTokenSource? newCts = null;

            //
            // 2. Serialize access to the chat via executionLock
            await executionLock.WaitAsync(externalCt).ConfigureAwait(false);

            try
            {
                //
                // 3. Create CTS for this batch, linked to externalCt
                //
                newCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
                var token = newCts.Token;

                // Publish this CTS as "current"
                Interlocked.Exchange(ref currentExecutionCts, newCts); // active thread will place its CTS here
                if (batches.TryDequeue(out var batch)) // we must peak the batch for this execution
                {
                    logger.LogDebugMessage($"[ChatBatchExecutor] RunActualTask: Dequeued batch with {batch.Messages.Count} messages, {batch.Commands.Count} commands for chat {chatId}");

                    // All messages must be added even if cancelled
                    await AddMessages(batch).ConfigureAwait(false); // and add messages anyway
                    token.ThrowIfCancellationRequested(); // if cancellef next thread will pick up next batch

                    //
                    // 4. Execute the batch while holding the lock
                    //
                    // only the last thread will find the queue empty and exit after its execution
                    if (batches.IsEmpty)
                        await ExecutePipeline(batch, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogDebugMessage($"[ChatBatchExecutor] RunActualTask: Cancelled for chat {chatId}");
                throw;
            }
            finally
            {

                // 5. Clean up CTS and release lock                
                if (newCts != null)
                {
                    // Only clear currentExecutionCts if it still refers to this CTS.
                    Interlocked.CompareExchange(ref currentExecutionCts, null, newCts);
                    newCts.Dispose();
                }

                executionLock.Release();
            }
        }

        private async Task ExecutePipeline(EventBatch batch, CancellationToken ct)
        {
            // Phase 1: Expire (only if it's the single event in batch)
            if (batch.IsOnlyExpire)
            {
                logger.LogDebugMessage($"[ChatBatchExecutor] Phase 1: Processing Expire (Reset) for chat {chat.Id}");
                await chat.Reset().ConfigureAwait(false);
                return;
            }

            // Phase 2a: CtrlC events (each with DoResponse)
            if (batch.CtrlCEvents.Count > 0)
            {
                logger.LogDebugMessage($"[ChatBatchExecutor] Phase 2a: Processing {batch.CtrlCEvents.Count} CtrlC events for chat {chat.Id}");
                foreach (var ctrlC in batch.CtrlCEvents)
                {
                    ct.ThrowIfCancellationRequested();
                    var messageAdded = await ProcessCtrlCAsync(ctrlC, ct).ConfigureAwait(false);
                    if (messageAdded)
                    {
                        await chat.DoResponseToLastMessage(ct).ConfigureAwait(false);
                    }
                    return;
                }
            }

            // Phase 2b: CtrlV events (each with DoResponse)
            if (batch.CtrlVEvents.Count > 0)
            {
                logger.LogDebugMessage($"[ChatBatchExecutor] Phase 2b: Processing {batch.CtrlVEvents.Count} CtrlV events for chat {chat.Id}");
                foreach (var ctrlV in batch.CtrlVEvents)
                {
                    ct.ThrowIfCancellationRequested();
                    var messageAdded = await ProcessCtrlVAsync(ctrlV, ct).ConfigureAwait(false);
                    if (messageAdded)
                    {
                        await chat.DoResponseToLastMessage(ct).ConfigureAwait(false);
                    }
                    return;
                }
            }

            // Phase 3: Commands (all in sequence)
            if (batch.Commands.Count > 0)
            {
                logger.LogDebugMessage($"[ChatBatchExecutor] Phase 3: Processing {batch.Commands.Count} commands for chat {chat.Id}");
                foreach (var cmd in batch.Commands)
                {
                    ct.ThrowIfCancellationRequested();
                    await cmd.Command.Execute(chat, cmd.Message, ct).ConfigureAwait(false);
                }
            }

            // Phase 4: Actions (only last, only if no messages)
            if (batch.LastAction != null && batch.Messages.Count == 0)
            {
                logger.LogDebugMessage($"[ChatBatchExecutor] Phase 4: Processing last action for chat {chat.Id}");
                ct.ThrowIfCancellationRequested();
                await chatMessageActionProcessor.HandleMessageAction(chat, batch.LastAction.ActionParameters, ct).ConfigureAwait(false);
                return;
            }

            // Phase 5: Messages (all added, one DoResponse at the end)
            if (batch.Messages.Count > 0)
            {
                logger.LogDebugMessage($"[ChatBatchExecutor] Phase 5: Processing {batch.Messages.Count} messages for chat {chat.Id}");
                ct.ThrowIfCancellationRequested();
                await chat.DoResponseToLastMessage(ct).ConfigureAwait(false);
                return;
            }
        }

        private async Task AddMessages(EventBatch batch)
        {
            var chatMessages = batch.Messages.Select(m => m.Message).ToList();
            if (chatMessages.Count == 0)
            {
                return;
            }

            // Add to pending queue before processing (safety net for cancellation)
            await chat.AddMessages(chatMessages).ConfigureAwait(false);
        }

        private async Task<bool> ProcessCtrlCAsync(EventChatCtrlCHotkey ctrlC, CancellationToken ct)
        {
            if (screenshotProvider == null)
            {
                logger.LogDebugMessage($"[ChatBatchExecutor] CtrlC event received but screenshotProvider is null");
                return false;
            }

            var imageBytes = await screenshotProvider.CaptureScreenAsync(ct).ConfigureAwait(false);
            if (imageBytes.Length == 0)
            {
                logger.LogDebugMessage($"[ChatBatchExecutor] CtrlC screenshot is empty");
                return false;
            }
            var imageBase64 = Convert.ToBase64String(Helpers.ConvertImageBytesToWebp(imageBytes));

            if (string.IsNullOrEmpty(imageBase64))
            {
                logger.LogDebugMessage($"[ChatBatchExecutor] CtrlC screenshot conversion produced empty payload");
                return false;
            }

            await chat.AddMessages([
                new ChatMessageModel(
                [
                    new ImageContentItem { ImageInBase64 = imageBase64 },
                    new TextContentItem { Text = "Please find a bug in my solution." }
                ], MessageRole.eRoleUser)
            ]).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> ProcessCtrlVAsync(EventChatCtrlVHotkey ctrlV, CancellationToken ct)
        {
            if (screenshotProvider == null)
            {
                logger.LogDebugMessage($"[ChatBatchExecutor] CtrlV event received but screenshotProvider is null");
                return false;
            }

            var imageBytes = await screenshotProvider.CaptureScreenAsync(ct).ConfigureAwait(false);
            if (imageBytes.Length == 0)
            {
                logger.LogDebugMessage($"[ChatBatchExecutor] CtrlV screenshot is empty");
                return false;
            }
            var imageBase64 = Convert.ToBase64String(Helpers.ConvertImageBytesToWebp(imageBytes));

            if (string.IsNullOrEmpty(imageBase64))
            {
                logger.LogDebugMessage($"[ChatBatchExecutor] CtrlV screenshot conversion produced empty payload");
                return false;
            }

            await chat.AddMessages([
                new ChatMessageModel(
                [
                    new ImageContentItem { ImageInBase64 = imageBase64 },
                    new TextContentItem { Text = "Please write a chain of thoughts on how I should think to solve the coding problem." }
                ], MessageRole.eRoleUser)
            ]).ConfigureAwait(false);
            return true;
        }

        private static EventBatch ClassifyEvents(IEnumerable<IChatEvent> events)
        {
            var batch = new EventBatch();
            var orderedEvents = events.OrderBy(e => e.OrderId).ToArray();

            foreach (var e in orderedEvents)
            {
                switch (e)
                {
                    case EventChatMessage m: batch.Messages.Add(m); break;
                    case EventChatCommand c: batch.Commands.Add(c); break;
                    case EventChatAction a: batch.Actions.Add(a); break;
                    case EventChatExpire exp: batch.Expires.Add(exp); break;
                    case EventChatCtrlCHotkey ctrlC: batch.CtrlCEvents.Add(ctrlC); break;
                    case EventChatCtrlVHotkey ctrlV: batch.CtrlVEvents.Add(ctrlV); break;
                }
            }

            batch.IsOnlyExpire = batch.Expires.Count > 0 && orderedEvents.Length == 1;
            batch.LastAction = batch.Actions.Count > 0 ? batch.Actions.Last() : null;

            return batch;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            var cts = Interlocked.Exchange(ref currentExecutionCts, null);
            cts?.Cancel();
            cts?.Dispose();

            executionLock.Dispose();
        }

        private sealed class EventBatch
        {
            public List<EventChatMessage> Messages { get; } = [];
            public List<EventChatCommand> Commands { get; } = [];
            public List<EventChatAction> Actions { get; } = [];
            public List<EventChatExpire> Expires { get; } = [];
            public List<EventChatCtrlCHotkey> CtrlCEvents { get; } = [];
            public List<EventChatCtrlVHotkey> CtrlVEvents { get; } = [];

            public bool IsOnlyExpire { get; set; }
            public EventChatAction? LastAction { get; set; }
        }
    }
}