using System.Collections.Concurrent;
using System.Reactive.Linq;

namespace ChatWithAI.Core
{
    public sealed class ChatEventBatcher(
        AccessChecker accessChecker,
        IChatActionEventSource chatActionEventSource,
        IChatMessageEventSource chatMessageEventSource,
        IChatCommandEventSource chatCommandEventSource,
        IChatExpireEventSource chatExpireEventSource,
        IChatCtrlCEventSource? chatCtrlCEventSource,
        IChatCtrlVEventSource? chatCtrlVEventSource,
        IScreenshotProvider? screenshotProvider,
        IChatModeLoader chatModeLoader,
        IChatFactory chatFactory,
        IChatMessageActionProcessor chatMessageActionProcessor,
        IMessenger messenger,
        ILogger logger) : IChatProcessor, IDisposable
    {
        private readonly ConcurrentDictionary<string, Lazy<Task<IChat?>>> _chatCache = new();
        private readonly ConcurrentDictionary<string, ChatBatchExecutor> chatExecutors = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> chatCreationSemaphores = new();
        private IDisposable? eventsSubscription;

        public async Task RunEventLoop(CancellationToken cancellationToken = default)
        {
            await chatActionEventSource.Run().ConfigureAwait(false);

            var eventSources = new List<IObservable<IChatEvent>>
            {
                chatActionEventSource.ChatActions,
                chatMessageEventSource.ChatMessages,
                chatCommandEventSource.ChatCommands,
                chatExpireEventSource.ExpireChats
            };

            // Only add Ctrl+C and Ctrl+V event sources if they are not null
            if (chatCtrlCEventSource != null)
                eventSources.Add(chatCtrlCEventSource.CtrlCActions);

            if (chatCtrlVEventSource != null)
                eventSources.Add(chatCtrlVEventSource.CtrlVActions);

            var allEvents = Observable.Merge(eventSources.ToArray());

            eventsSubscription = allEvents
                .GroupBy(e => e.ChatId)
                .SelectMany(group =>
                    group.Buffer(TimeSpan.FromMilliseconds(250), 100)
                         .Where(buf => buf.Any())
                         .SelectMany(buf =>
                             Observable.FromAsync(ct => ProcessChatEventsAsync(group.Key, buf, ct))))
                .Subscribe(
                    _ => { },
                    ex => logger.LogDebugMessage($"Event processing stopped: {ex}"),
                    () => logger.LogDebugMessage("Event processing completed."));

            logger.LogDebugMessage("ChatEventProcessor started.");

            await Task.Delay(Timeout.Infinite, cancellationToken)
                       .ContinueWith(_ =>
                       {
                           eventsSubscription.Dispose();
                       }, TaskScheduler.Default).ConfigureAwait(false);
        }

        private async Task ProcessChatEventsAsync(string chatId, IEnumerable<IChatEvent> events, CancellationToken cancellationToken)
        {
            // Extract username for access check
            var firstEvent = events.FirstOrDefault();
            var username = firstEvent switch
            {
                EventChatMessage m => m.Username,
                EventChatCommand c => c.Username,
                _ => "_"
            };

            // Check access
            if (!await accessChecker.HasAccessAsync(chatId, username).ConfigureAwait(false))
            {
                await messenger.SendTextMessage(chatId, new MessengerMessageDTO { TextContent = Strings.NoAccess }).ConfigureAwait(false);
                return;
            }

            // Get or create chat instance
            var chat = await GetOrCreateChatAsync(chatId).ConfigureAwait(false);
            if (chat == null) return;

            // Get or create executor for this chat
            var executor = chatExecutors.GetOrAdd(chatId, _ =>
                new ChatBatchExecutor(chat, screenshotProvider, chatMessageActionProcessor, logger));

            try
            {
                // Execute batch using dedicated executor
                await executor.ExecuteBatch(chatId, events, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                logger.LogDebugMessage($"[ProcessChatEventsAsync] Batch cancelled for chat {chatId}.");
            }
            catch (Exception ex)
            {
                logger.LogDebugMessage($"[ProcessChatEventsAsync] Error for chat {chatId}: {ex}");
            }
        }

        private async ValueTask<IChat?> GetOrCreateChatAsync(string chatId)
        {
            var lazyTask = _chatCache.GetOrAdd(chatId, id =>
                new Lazy<Task<IChat?>>(
                    () => CreateChatInternalAsync(id),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                return await lazyTask.Value.ConfigureAwait(false);
            }
            catch (Exception)
            {
                _chatCache.TryRemove(chatId, out _);
                return null;
            }
        }

        private async Task<IChat?> CreateChatInternalAsync(string chatId)
        {
            try
            {
                var chatMode = await chatModeLoader.GetChatMode("common").ConfigureAwait(false);
                var isPremiumUser = await accessChecker.IsPremiumUserAsync(chatId).ConfigureAwait(false);

                return await chatFactory.CreateChat(chatId, chatMode, !isPremiumUser).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                    logger.LogDebugMessage($"Chat creation cancelled for {chatId}");
                else
                    logger.LogDebugMessage($"Chat creation failed for {chatId}: {ex}");

                throw;
            }
        }

        public void Dispose()
        {
            eventsSubscription?.Dispose();

            foreach (var executor in chatExecutors.Values)
            {
                executor.Dispose();
            }
            chatExecutors.Clear();

            foreach (var sem in chatCreationSemaphores.Values) sem.Dispose();
            chatCreationSemaphores.Clear();

            logger.LogDebugMessage("ChatEventProcessor disposed.");
        }
    }
}
