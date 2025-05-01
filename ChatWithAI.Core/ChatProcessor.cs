using ChatWithAI.Core.ChatCommands;
using RxTelegram.Bot;
using RxTelegram.Bot.Interface.BaseTypes;
using System.Collections.Concurrent;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;

namespace ChatWithAI.Core
{
    public sealed class ChatProcessor(
        AccessChecker accessChecker,
        ConcurrentDictionary<string, ConcurrentDictionary<string, ActionId>> actionsMappingByChat,
        IChatFactory chatFactory,
        IChatMessageProcessor chatMessageProcessor,
        IChatMessageActionProcessor chatMessageActionProcessor,
        IChatMessageConverter chatMessageConverter,
        IMessengerBotSource botSource,
        CacheWithExpirationCallback cacheWithExpirationCallback,
        ILogger logger) : IChatProcessor, IDisposable
    {
        private sealed class ProcessingEvent
        {
            public string Type { get; }
            public string ChatId { get; }
            public Message? Message { get; }
            public CallbackQuery? CallbackQuery { get; }
            public ExpirationEventArgs? ExpirationArgs { get; }

            public ProcessingEvent(string type, string chatId, Message? message = null,
                CallbackQuery? callbackQuery = null, ExpirationEventArgs? expirationArgs = null)
            {
                Type = type;
                ChatId = chatId;
                Message = message;
                CallbackQuery = callbackQuery;
                ExpirationArgs = expirationArgs;
            }
        }

        private readonly ConcurrentDictionary<string, IChat> m_chatInstances = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> m_chatCreationSemaphores = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> m_chatProcessingCancellationSources = new();
        private readonly SemaphoreSlim m_telegramInstanceGuard = new(1, 1);
        private ITelegramBot? m_currentBot;
        private CompositeDisposable m_currentSubscriptions = [];
        private long m_telegramBotState = BotState.RequiresInitialization;

        private static readonly CompositeFormat HasStartedFormat = CompositeFormat.Parse(Strings.HasStarted);

        private static class BotState
        {
            public const long Running = 0;
            public const long RequiresInitialization = 1;
        }

        public async Task RunEventLoop(CancellationToken cancellationToken = default)
        {
            using var cancellationRegistration = cancellationToken.Register(Dispose);
            await EnsureTelegramListenerIsRunning(cancellationToken).ConfigureAwait(false);
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                logger?.LogInfoMessage("ChatProcessor processing was cancelled.");
            }
            finally
            {
                Dispose();
            }
        }

        private async Task ProcessCacheExpirationAsync(string chatId, CancellationToken cancellationToken)
        {
            if (m_chatInstances.TryGetValue(chatId, out var chat))
            {
                try
                {
                    logger?.LogInfoMessage($"Processing cache expiration for chatId '{chatId}' via Rx pipeline");
                    await ReStart.Execute(chat, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger?.LogInfoMessage($"Error processing cache expiration for chatId '{chatId}' via Rx pipeline: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            logger?.LogInfoMessage("Disposing ChatProcessor...");
            Interlocked.Exchange(ref m_telegramBotState, BotState.RequiresInitialization);
            m_currentSubscriptions.Dispose();

            foreach (var cts in m_chatProcessingCancellationSources.Values)
            {
                try
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                catch { }
            }
            m_chatProcessingCancellationSources.Clear();

            foreach (var semaphore in m_chatCreationSemaphores.Values) semaphore.Dispose();
            m_chatCreationSemaphores.Clear();
            foreach (var chat in m_chatInstances.Values) (chat as IDisposable)?.Dispose();
            m_chatInstances.Clear();
            m_telegramInstanceGuard.Dispose();
            logger?.LogInfoMessage("ChatProcessor disposed.");
        }

        private async Task EnsureTelegramListenerIsRunning(CancellationToken cancellationToken)
        {
            await m_telegramInstanceGuard.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Interlocked.Read(ref m_telegramBotState) == BotState.Running) return;
                if (cancellationToken.IsCancellationRequested) return;

                logger?.LogInfoMessage("Initializing or recreating Telegram listener...");
                m_currentSubscriptions.Dispose();
                m_currentSubscriptions = new CompositeDisposable();

                var botObject = botSource.NewBot();
                m_currentBot = botObject as ITelegramBot
                    ?? throw new InvalidCastException($"IMessengerBotSource returned an object of type '{botObject?.GetType().FullName ?? "null"}' which cannot be cast to ITelegramBot.");

                InitializeUnifiedProcessingPipeline(cancellationToken);

                var me = await m_currentBot.GetMe(cancellationToken).ConfigureAwait(false);
                logger?.LogInfoMessage(string.Format(CultureInfo.InvariantCulture, HasStartedFormat, me?.Username ?? "Bot"));

                Interlocked.Exchange(ref m_telegramBotState, BotState.Running);
                logger?.LogInfoMessage("Telegram listener started successfully.");
            }
            catch (OperationCanceledException)
            {
                logger?.LogInfoMessage("Telegram listener initialization cancelled.");
                Interlocked.Exchange(ref m_telegramBotState, BotState.RequiresInitialization);
            }
            catch (Exception exception)
            {
                logger?.LogInfoMessage($"Error initializing Telegram listener: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
                Interlocked.Exchange(ref m_telegramBotState, BotState.RequiresInitialization);
            }
            finally
            {
                m_telegramInstanceGuard.Release();
            }
        }

        private void InitializeUnifiedProcessingPipeline(CancellationToken cancellationToken)
        {
            var cancellationSignal = Observable.Create<Unit>(observer =>
                cancellationToken.Register(() =>
                {
                    observer.OnCompleted();
                }));

            var messageEvents = m_currentBot!.Updates.Message
                .Where(message => IsValidMessage(message))
                .Buffer(TimeSpan.FromMilliseconds(100), 5)
                .Where(messages => messages.Count > 0)
                .SelectMany(messages => messages
                    .OrderBy(m => m.MessageId)
                    .Select(message => new ProcessingEvent(
                        "Message",
                        message.Chat.Id.ToString(CultureInfo.InvariantCulture),
                        message: message
                    ))
                );

            var callbackEvents = m_currentBot!.Updates.CallbackQuery
                .Where(callbackQuery => IsValidCallbackQuery(callbackQuery))
                .Buffer(TimeSpan.FromMilliseconds(100), 5)
                .Where(callbacks => callbacks.Count > 0)
                .SelectMany(callbacks => callbacks
                    .OrderBy(cq => cq.Id)
                    .Select(callbackQuery => new ProcessingEvent(
                        "Callback",
                        callbackQuery.From.Id.ToString(CultureInfo.InvariantCulture),
                        callbackQuery: callbackQuery
                    ))
                );

            var expirationEvents = cacheWithExpirationCallback.ExpirationObservable
                .Select(args => new ProcessingEvent(
                    "Expiration",
                    ExtractChatIdFromCacheKey(args.Key),
                    expirationArgs: args
                ));

            var allEvents = Observable.Merge(messageEvents, callbackEvents, expirationEvents);

            IDisposable unifiedSubscription = allEvents
                .GroupBy(evt => evt.ChatId)
                .Select(group => group
                    .Select(evt =>
                    {
                        if (m_chatProcessingCancellationSources.TryGetValue(evt.ChatId, out var existingCts))
                        {
                            try
                            {
                                existingCts.Cancel();
                                existingCts.Dispose();
                            }
                            catch (Exception ex)
                            {
                                logger?.LogInfoMessage($"Error cancelling previous processing for chat {evt.ChatId}: {ex.Message}");
                            }
                        }

                        var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        m_chatProcessingCancellationSources[evt.ChatId] = linkedSource;

                        return Observable.FromAsync(async _ =>
                        {
                            try
                            {
                                var currentCts = m_chatProcessingCancellationSources.GetOrAdd(evt.ChatId, _ => linkedSource);
                                var currentToken = currentCts.Token;

                                switch (evt.Type)
                                {
                                    case "Message":
                                        await ProcessChatMessageAsync(evt.ChatId, evt.Message!, currentToken).ConfigureAwait(false);
                                        break;
                                    case "Callback":
                                        await ProcessCallbackQueryAsync(evt.ChatId, evt.CallbackQuery!, currentToken).ConfigureAwait(false);
                                        break;
                                    case "Expiration":
                                        await ProcessCacheExpirationAsync(evt.ChatId, currentToken);
                                        break;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                logger?.LogInfoMessage($"Processing of {evt.Type} for {evt.ChatId} was cancelled");
                            }
                            catch (Exception ex)
                            {
                                logger?.LogInfoMessage($"Error processing {evt.Type} for {evt.ChatId}: {ex.Message}");
                                if (m_chatInstances.TryGetValue(evt.ChatId, out var chat))
                                {
                                    chat?.RecreateAiAgent();
                                }
                            }
                            finally
                            {
                                if (m_chatProcessingCancellationSources.TryGetValue(evt.ChatId, out var currentCts) &&
                                    currentCts == linkedSource)
                                {
                                    m_chatProcessingCancellationSources.TryRemove(evt.ChatId, out var _);
                                    linkedSource.Dispose();
                                }
                            }
                            return Unit.Default;
                        });
                    })
                    .Concat()
                )
                .Merge()
                .TakeUntil(cancellationSignal)
                .Subscribe(
                    _ => { },
                    exception => HandlePipelineError(exception, "UnifiedProcessing"),
                    () =>
                    {
                        logger?.LogInfoMessage("Unified processing stream completed.");
                        foreach (var cts in m_chatProcessingCancellationSources.Values)
                        {
                            try
                            {
                                cts.Cancel();
                                cts.Dispose();
                            }
                            catch { }
                        }
                        m_chatProcessingCancellationSources.Clear();
                    }
                );

            m_currentSubscriptions.Add(unifiedSubscription);
            m_currentSubscriptions.Add(Disposable.Create(() =>
            {
                foreach (var cts in m_chatProcessingCancellationSources.Values)
                {
                    try
                    {
                        cts.Cancel();
                        cts.Dispose();
                    }
                    catch { }
                }
                m_chatProcessingCancellationSources.Clear();
            }));
        }

        private static string ExtractChatIdFromCacheKey(string key)
        {
            const string chatTurnsPrefix = "chat_turns_";

            if (key.StartsWith(chatTurnsPrefix, StringComparison.Ordinal))
            {
                return key.Substring(chatTurnsPrefix.Length);
            }

            return "messenger";
        }

        private static bool IsValidMessage(Message? message)
        {
            if (message?.Chat == null || message.From == null) return false;
            if (message.From.Id != message.Chat.Id) return false;
            bool hasText = !string.IsNullOrEmpty(message.Text) || !string.IsNullOrEmpty(message.Caption) ||
                           !string.IsNullOrEmpty(message.ReplyToMessage?.Text) || !string.IsNullOrEmpty(message.ReplyToMessage?.Caption) ||
                           message.Sticker != null;
            bool hasPhoto = message.Photo != null || message.ReplyToMessage?.Photo != null;
            bool hasAudio = message.Audio != null;
            return hasText || hasPhoto || hasAudio;
        }

        private static bool IsValidCallbackQuery(CallbackQuery? callbackQuery)
        {
            return callbackQuery?.From?.Id != null && !string.IsNullOrEmpty(callbackQuery.Data);
        }

        private async Task ProcessChatMessageAsync(string chatId, Message rawMessage, CancellationToken cancellationToken)
        {
            IChat? chat = null;
            try
            {
                chat = await GetOrCreateChatAsync(chatId, cancellationToken).ConfigureAwait(false);
                if (chat == null || cancellationToken.IsCancellationRequested) return;

                if (!accessChecker.HasAccess(chatId, rawMessage.From))
                {
                    await chat.SendSystemMessage(Strings.NoAccess, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var chatMessage = await chatMessageConverter.ConvertToChatMessage(rawMessage, cancellationToken).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested) return;

                await chatMessageProcessor.HandleMessage(chat, chatMessage, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                logger?.LogInfoMessage($"[ProcessChatMessageAsync] Task cancelled for chat {chatId}.");
            }
            catch (Exception exception)
            {
                logger?.LogInfoMessage($"[ProcessChatMessageAsync] Error for chat {chatId}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
                chat?.RecreateAiAgent();
            }
        }

        private async Task ProcessCallbackQueryAsync(string chatId, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            IChat? chat = null;
            try
            {
                if (!actionsMappingByChat.TryGetValue(chatId, out var chatActions) ||
                    !chatActions.TryRemove(callbackQuery.Data, out var actionId)) return;
                if (!m_chatInstances.TryGetValue(chatId, out chat)) return;
                if (cancellationToken.IsCancellationRequested) return;

                var actionParameters = new ActionParameters(
                    actionId,
                    callbackQuery.Message?.MessageId.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

                await chatMessageActionProcessor.HandleMessageAction(chat, actionParameters, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                logger?.LogInfoMessage($"[ProcessCallbackQueryAsync] Task cancelled for chat {chatId}, data: {callbackQuery.Data}.");
            }
            catch (Exception exception)
            {
                logger?.LogInfoMessage($"[ProcessCallbackQueryAsync] Error for chat {chatId}, data: {callbackQuery.Data}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
                chat?.RecreateAiAgent();
            }
        }

        private async Task<IChat?> GetOrCreateChatAsync(string chatId, CancellationToken cancellationToken)
        {
            if (m_chatInstances.TryGetValue(chatId, out var existingChat)) return existingChat;
            var semaphore = m_chatCreationSemaphores.GetOrAdd(chatId, key => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_chatInstances.TryGetValue(chatId, out existingChat)) return existingChat;
                if (cancellationToken.IsCancellationRequested) return null;
                var newChat = await chatFactory.CreateChat(chatId, "common", !accessChecker.IsPremiumUser(chatId)).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    (newChat as IDisposable)?.Dispose();
                    return null;
                }
                if (m_chatInstances.TryAdd(chatId, newChat)) return newChat;
                else
                {
                    (newChat as IDisposable)?.Dispose();
                    m_chatInstances.TryGetValue(chatId, out existingChat);
                    return existingChat;
                }
            }
            catch (OperationCanceledException)
            {
                logger?.LogInfoMessage($"Chat creation cancelled for chat ID: {chatId}");
                return null;
            }
            catch (Exception exception)
            {
                logger?.LogInfoMessage($"Failed to create chat for ID: {chatId}. Error: {exception.Message}");
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        }

        private void HandlePipelineError(Exception exception, string pipelineName)
        {
            if (exception is OperationCanceledException)
            {
                logger?.LogInfoMessage($"[{pipelineName}] Rx pipeline subscription cancelled.");
                return;
            }
            logger?.LogInfoMessage($"[{pipelineName}] Unhandled error in Rx pipeline: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
            if (Interlocked.CompareExchange(ref m_telegramBotState, BotState.RequiresInitialization, BotState.Running) == BotState.Running)
            {
                logger?.LogInfoMessage($"[{pipelineName}] Marking bot as broken and attempting to recreate listener.");
                _ = Task.Run(() => EnsureTelegramListenerIsRunning(CancellationToken.None));
            }
            else
            {
                logger?.LogInfoMessage($"[{pipelineName}] Listener already marked as broken or recreation is in progress.");
            }
        }
    }
}