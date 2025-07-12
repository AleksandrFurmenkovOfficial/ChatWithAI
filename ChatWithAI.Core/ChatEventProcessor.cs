using ChatWithAI.Core.ChatCommands;
using System.Collections.Concurrent;
using System.Reactive.Linq;

namespace ChatWithAI.Core
{
    public sealed class ChatEventProcessor(
        AccessChecker accessChecker,
        IChatActionEventSource chatActionEventSource,
        IChatMessageEventSource chatMessageEventSource,
        IChatCommandEventSource chatCommandEventSource,
        IChatExpireEventSource chatExpireEventSource,
        IChatCtrlCEventSource? chatCtrlCEventSource,
        IChatCtrlVEventSource? chatCtrlVEventSource,
        IScreenshotProvider? screenshotProvider,
        IChatFactory chatFactory,
        IChatMessageActionProcessor chatMessageActionProcessor,
        ILogger logger) : IChatProcessor, IDisposable
    {
        private readonly ConcurrentDictionary<string, IChat> chatInstances = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> chatProcessingCts = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> chatCreationSemaphores = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> chatProcessingSemaphores = new();
        private IDisposable? eventsSubscription;

        public Task RunEventLoop(CancellationToken cancellationToken = default)
        {
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
                    group.Buffer(TimeSpan.FromMilliseconds(100), 100)
                         .Where(buf => buf.Any())
                         .SelectMany(buf =>
                             Observable.FromAsync(ct => ProcessChatEventsAsync(group.Key, buf, ct))))
                .Subscribe(
                    _ => { },
                    ex => logger.LogInfoMessage($"Event processing stopped: {ex}"),
                    () => logger.LogInfoMessage("Event processing completed."));

            logger.LogInfoMessage("ChatEventProcessor started.");

            return Task.Delay(Timeout.Infinite, cancellationToken)
                       .ContinueWith(_ =>
                       {
                           eventsSubscription.Dispose();
                       }, TaskScheduler.Default);
        }

        private async Task ProcessChatEventsAsync(string chatId, IEnumerable<IChatEvent> events, CancellationToken cancellationToken)
        {
            var semaphore = chatProcessingSemaphores.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            CancellationTokenSource? oldCts = null;
            CancellationTokenSource newCts;
            try
            {
                chatProcessingCts.TryGetValue(chatId, out oldCts);
                newCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                chatProcessingCts[chatId] = newCts;
            }
            finally
            {
                semaphore.Release();
            }

            if (oldCts != null)
            {
                await oldCts.CancelAsync().ConfigureAwait(false);
                oldCts.Dispose();
            }

            var token = newCts.Token;
            token.ThrowIfCancellationRequested();

            var messages = new List<EventChatMessage>();
            var commands = new List<EventChatCommand>();
            var actions = new List<EventChatAction>();
            var expires = new List<EventChatExpire>();
            var ctrlCActions = new List<EventChatCtrlCHotkey>();
            var ctrlVActions = new List<EventChatCtrlVHotkey>();

            var orderedEvents = events.OrderBy(e => e.OrderId).ToArray();
            foreach (var e in orderedEvents)
            {
                switch (e)
                {
                    case EventChatMessage m: messages.Add(m); break;
                    case EventChatCommand c: commands.Add(c); break;
                    case EventChatAction a: actions.Add(a); break;
                    case EventChatExpire exp: expires.Add(exp); break;
                    case EventChatCtrlCHotkey ctrlC: ctrlCActions.Add(ctrlC); break;
                    case EventChatCtrlVHotkey ctrlV: ctrlVActions.Add(ctrlV); break;
                }
            }

            var chatMessages = messages.Select(m => m.Message).ToList();
            IChat? chat = null;
            try
            {
                chat = await GetOrCreateChatAsync(chatId).ConfigureAwait(false);
                if (chat == null) return;

                token.ThrowIfCancellationRequested();
                if (expires.Count > 0 && orderedEvents.Length == 1)
                {
                    await ReStart.Execute(chat, default).ConfigureAwait(false);
                }

                // !!!
                // PART to support ChatWithAI.Plugins.Windows.ScreenshotCapture
                // Feel free to remove it for server usage!
                await ProcessHotkeys(screenshotProvider, ctrlCActions, ctrlVActions, chat, token).ConfigureAwait(false);
                // END OF 
                // PART to support ChatWithAI.Plugins.Windows.ScreenshotCapture
                // Feel free to remove it for server usage!
                // !!!

                var username = messages.Count > 0 ? messages[0].Username :
                               commands.Count > 0 ? commands[0].Username : "_";
                if (!accessChecker.HasAccess(chatId, username))
                {
                    await chat.SendSystemMessage(Strings.NoAccess, token).ConfigureAwait(false);
                    return;
                }

                foreach (var cmd in commands)
                {
                    token.ThrowIfCancellationRequested();
                    await cmd.Command.Execute(chat, cmd.Message, token).ConfigureAwait(false);
                }

                var lastAction = actions.Count > 0 ? actions.Last() : null;
                if (lastAction != null && messages.Count == 0)
                {
                    token.ThrowIfCancellationRequested();
                    await chatMessageActionProcessor.HandleMessageAction(chat, lastAction.ActionParameters, token).ConfigureAwait(false);
                }

                if (chatMessages.Count > 0)
                {
                    chat.AddMessages(chatMessages);
                    chatMessages.Clear();

                    token.ThrowIfCancellationRequested();
                    await chat.DoResponseToLastMessage(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInfoMessage($"[ProcessChatEventsAsync] Cancelled for chat {chatId}.");
            }
            catch (Exception ex)
            {
                logger.LogInfoMessage($"[ProcessChatEventsAsync] Error for chat {chatId}: {ex}");
                chat?.RecreateAiAgent();
            }
            finally
            {
                if (chat != null && chatMessages.Count > 0)
                {
                    chat.AddMessages(chatMessages);
                }

                if (chatProcessingCts.TryRemove(new KeyValuePair<string, CancellationTokenSource>(chatId, newCts)))
                {
                    newCts.Dispose();
                }
            }
        }

        private static async Task ProcessHotkeys(IScreenshotProvider? screenshotProvider, List<EventChatCtrlCHotkey> ctrlCActions, List<EventChatCtrlVHotkey> ctrlVActions, IChat chat, CancellationToken token)
        {
            if (ctrlCActions.Count > 0 && screenshotProvider != null)
            {
                var imageBytes = await screenshotProvider.CaptureScreenAsync(default).ConfigureAwait(false);
                var imageBase64 = Convert.ToBase64String(Helpers.ConvertImageBytesToWebp(imageBytes));
                if (!string.IsNullOrEmpty(imageBase64))
                {
                    chat.AddMessages([
                         new ChatMessage(
                         [
                             new ImageContentItem { ImageInBase64 = imageBase64 },
                             new TextContentItem { Text = "Please find a bug in my solution." }
                         ], MessageRole.eRoleUser)
                    ]);

                    await chat.DoResponseToLastMessage(token).ConfigureAwait(false);
                }
            }

            if (ctrlVActions.Count > 0 && screenshotProvider != null)
            {
                var imageBytes = await screenshotProvider.CaptureScreenAsync(default).ConfigureAwait(false);
                var imageBase64 = Convert.ToBase64String(Helpers.ConvertImageBytesToWebp(imageBytes));
                if (!string.IsNullOrEmpty(imageBase64))
                {
                    chat.AddMessages([
                         new ChatMessage(
                         [
                             new ImageContentItem { ImageInBase64 = imageBase64 },
                             new TextContentItem { Text = "Please write a chain of thoughts on how I should think to solve the coding problem." }
                         ], MessageRole.eRoleUser)
                    ]);

                    await chat.DoResponseToLastMessage(token).ConfigureAwait(false);
                }
            }
        }

        private async ValueTask<IChat?> GetOrCreateChatAsync(string chatId)
        {
            if (chatInstances.TryGetValue(chatId, out var existing)) return existing;

            var semaphore = chatCreationSemaphores.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (chatInstances.TryGetValue(chatId, out existing)) return existing;
                var chat = await chatFactory.CreateChat(chatId, "common", !accessChecker.IsPremiumUser(chatId)).ConfigureAwait(false);
                if (chatInstances.TryAdd(chatId, chat)) return chat;
                (chat as IDisposable)?.Dispose();
                chatInstances.TryGetValue(chatId, out existing);
                return existing;
            }
            catch (OperationCanceledException)
            {
                logger.LogInfoMessage($"Chat creation cancelled for {chatId}");
                return null;
            }
            catch (Exception ex)
            {
                logger.LogInfoMessage($"Chat creation failed for {chatId}: {ex}");
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public void Dispose()
        {
            eventsSubscription?.Dispose();

            foreach (var cts in chatProcessingCts.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            chatProcessingCts.Clear();

            foreach (var chat in chatInstances.Values)
            {
                (chat as IDisposable)?.Dispose();
            }
            chatInstances.Clear();

            foreach (var sem in chatProcessingSemaphores.Values) sem.Dispose();
            chatProcessingSemaphores.Clear();

            foreach (var sem in chatCreationSemaphores.Values) sem.Dispose();
            chatCreationSemaphores.Clear();

            logger.LogInfoMessage("ChatEventProcessor disposed.");
        }
    }
}
