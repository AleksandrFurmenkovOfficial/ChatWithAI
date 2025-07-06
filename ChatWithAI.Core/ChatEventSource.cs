using RxTelegram.Bot;
using RxTelegram.Bot.Interface.BaseTypes;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace ChatWithAI.Core
{
    public sealed class ChatEventSource :
        IChatActionEventSource,
        IChatMessageEventSource,
        IChatCommandEventSource,
        IChatExpireEventSource,
        IDisposable
    {
        private readonly IChatMessageConverter chatMessageConverter;
        private readonly IAdminChecker adminChecker;
        private readonly Dictionary<string, IChatCommand> commands = [];
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ActionId>> actionsMappingByChat = new();

        private int disposed;
        private readonly ILogger? logger;
        private readonly CancellationTokenSource cancellationTokenSource = new();

        // Bot state management
        private static class BotState
        {
            public const long Running = 0;
            public const long RequiresInitialization = 1;
        }
        private ITelegramBot? bot;
        private long telegramBotState = BotState.RequiresInitialization;
        private readonly IMessengerBotSource telegramBotSource;

        // Add cache field
        private readonly ChatCache cache;

        // Subscription subjects
        private readonly CompositeDisposable subscriptions = [];

        private readonly Subject<EventChatAction> chatActionSubject = new();
        public IObservable<EventChatAction> ChatActions => chatActionSubject.AsObservable();

        private readonly Subject<EventChatMessage> chatMessageSubject = new();
        public IObservable<EventChatMessage> ChatMessages => chatMessageSubject.AsObservable();

        private readonly Subject<EventChatCommand> chatCommandSubject = new();
        public IObservable<EventChatCommand> ChatCommands => chatCommandSubject.AsObservable();

        // Add expire events subject and observable
        private readonly Subject<EventChatExpire> chatExpireSubject = new();
        public IObservable<EventChatExpire> ExpireChats => chatExpireSubject.AsObservable();

        // Update constructor to include cache parameter
        public ChatEventSource(List<IChatCommand> commands, ConcurrentDictionary<string, ConcurrentDictionary<string, ActionId>> actionsMappingByChat, IMessengerBotSource telegramBotSource, IChatMessageConverter chatMessageConverter, IAdminChecker adminChecker, ChatCache cache, ILogger logger)
        {
            foreach (var command in commands)
            {
                this.commands.Add($"/{command.Name}", command);
            }

            this.actionsMappingByChat = actionsMappingByChat ?? throw new ArgumentNullException(nameof(actionsMappingByChat));
            this.telegramBotSource = telegramBotSource ?? throw new ArgumentNullException(nameof(telegramBotSource));
            this.chatMessageConverter = chatMessageConverter ?? throw new ArgumentNullException(nameof(chatMessageConverter));
            this.adminChecker = adminChecker ?? throw new ArgumentNullException(nameof(adminChecker));
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            EnsureTelegramListenerIsRunning();
        }

        private static bool IsValidCallbackQuery(CallbackQuery? callbackQuery)
        {
            return callbackQuery?.From?.Id != null && !string.IsNullOrEmpty(callbackQuery.Data);
        }

        private static bool IsValidMessage(Message? message)
        {
            if (message?.Chat == null || message.From == null) return false;
            if (message.From.Id != message.Chat.Id) return false;
            bool hasText = !string.IsNullOrEmpty(message.Text) || !string.IsNullOrEmpty(message.Caption) ||
                           !string.IsNullOrEmpty(message.ReplyToMessage?.Text) || !string.IsNullOrEmpty(message.ReplyToMessage?.Caption) ||
                           message.Sticker != null;
            bool hasPhoto = message.Photo != null || message.ReplyToMessage?.Photo != null;
            bool hasAudio = message.Audio != null || message.Voice != null;
            return hasText || hasPhoto || hasAudio;
        }

        private void EnsureTelegramListenerIsRunning()
        {
            if (Interlocked.Read(ref telegramBotState) == BotState.Running) return;
            if (disposed != 0) return;

            // bot getting is well written and frozen, do not edit with AI!
            bot = telegramBotSource.NewBot() as ITelegramBot;
            if (bot == null)
            {
                throw new ArgumentNullException("The bot is not initialized.");
            }

            subscriptions.Clear();

            var callbackSubscription = bot.Updates.CallbackQuery
                .Where(IsValidCallbackQuery)
                .Buffer(TimeSpan.FromMilliseconds(25), 10)
                .Where(buffer => buffer.Count > 0)
                .SelectMany(buffer => buffer
                    .OrderBy(cq => cq.Id)
                    .GroupBy(cq => cq.From!.Id.ToString(CultureInfo.InvariantCulture))
                    .Select(chatGroup => chatGroup.Last())
                    .Select(callbackQuery =>
                    {
                        var chatId = callbackQuery.From.Id.ToString(CultureInfo.InvariantCulture);
                        if (!actionsMappingByChat.TryGetValue(chatId, out var chatActions) ||
                            !chatActions.TryRemove(callbackQuery.Data, out var actionId))
                            return null;

                        var actionParameters = new ActionParameters(actionId, callbackQuery.Message?.MessageId.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                        return new EventChatAction(
                            chatId,
                            actionParameters.MessageId,
                            actionParameters
                        );
                    })
                )
                .Subscribe(
                    eventChatAction =>
                    {
                        if (eventChatAction != null)
                        {
                            chatActionSubject.OnNext(eventChatAction);
                        }
                    },
                    error => HandlePipelineError(error, "CallbackQuery pipeline")
                );
            subscriptions.Add(callbackSubscription);

            var messageSubscription = bot.Updates.Message
                .Where(IsValidMessage)
                .Buffer(TimeSpan.FromMilliseconds(75), 100)
                .Where(buffer => buffer.Count > 0)
                .SelectMany(buffer => buffer
                    .OrderBy(m => m.MessageId)
                    .Select(message => Observable.FromAsync(async ct =>
                    {
                        var username = string.Join("_", message.From.FirstName, message.From.Username, message.From.LastName).Trim('_');
                        var chatMessage = await chatMessageConverter.ConvertToChatMessage(message, ct).ConfigureAwait(false);
                        var command = GetChatCommand(message.Chat.Id.ToString(CultureInfo.InvariantCulture), chatMessage, username);
                        if (command != null)
                        {
                            chatCommandSubject.OnNext(command);
                            return null;
                        }

                        return new EventChatMessage(
                            message.From.Id.ToString(CultureInfo.InvariantCulture),
                            message.MessageId.ToString(CultureInfo.InvariantCulture),
                            username,
                            chatMessage
                        );
                    }))
                )
                .Merge()
                .Subscribe(
                    eventChatMessage =>
                    {
                        if (eventChatMessage != null)
                        {
                            chatMessageSubject.OnNext(eventChatMessage);
                        }
                    },
                    error => HandlePipelineError(error, "Message pipeline")
                );
            subscriptions.Add(messageSubscription);

            var cacheExpirationSubscription = cache.ExpirationObservable
                .Subscribe(
                    expirationArgs =>
                    {
                        try
                        {
                            var eventChatExpire = new EventChatExpire(expirationArgs.ChatId);
                            chatExpireSubject.OnNext(eventChatExpire);
                        }
                        catch (Exception ex)
                        {
                            logger?.LogInfoMessage($"Error processing cache expiration event: {ex.Message}");
                        }
                    },
                    error => HandlePipelineError(error, "Cache expiration pipeline")
                );
            subscriptions.Add(cacheExpirationSubscription);

            Interlocked.Exchange(ref telegramBotState, BotState.Running);
        }

        private void HandlePipelineError(Exception exception, string pipelineName)
        {
            if (exception is OperationCanceledException)
            {
                logger?.LogInfoMessage($"[{pipelineName}] Rx pipeline subscription cancelled.");
                return;
            }
            logger?.LogInfoMessage($"[{pipelineName}] Unhandled error in Rx pipeline: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
            if (Interlocked.CompareExchange(ref telegramBotState, BotState.RequiresInitialization, BotState.Running) == BotState.Running)
            {
                logger?.LogInfoMessage($"[{pipelineName}] Marking bot as broken and attempting to recreate listener.");
                _ = Task.Run(() => EnsureTelegramListenerIsRunning());
            }
            else
            {
                logger?.LogInfoMessage($"[{pipelineName}] Listener already marked as broken or recreation is in progress.");
            }
        }

        private EventChatCommand? GetChatCommand(string chatId, ChatMessage message, string username)
        {
            if (message.Content == null || message.Content.Count == 0)
            {
                return null;
            }

            var textItem = ChatMessage.GetTextContentItem(message).FirstOrDefault();
            if (string.IsNullOrEmpty(textItem?.Text))
                return null;

            var text = textItem.Text;
            foreach ((string commandName, IChatCommand command) in commands.Where(value =>
                         text.Trim().Contains(value.Key, StringComparison.InvariantCultureIgnoreCase)))
            {
                if (command.IsAdminOnlyCommand && !adminChecker.IsAdmin(chatId))
                {
                    return null;
                }

                textItem.Text = text[commandName.Length..];
                return new EventChatCommand(
                    chatId,
                    message.Id.Value,
                    username,
                    command,
                    message,
                    textItem.Text
                );
            }

            return null;
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref disposed, 1, 0) != 0) return;

            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();

            chatActionSubject?.OnCompleted();
            chatActionSubject?.Dispose();

            chatMessageSubject?.OnCompleted();
            chatMessageSubject?.Dispose();

            chatCommandSubject?.OnCompleted();
            chatCommandSubject?.Dispose();

            chatExpireSubject?.OnCompleted();
            chatExpireSubject?.Dispose();

            subscriptions?.Dispose();
        }
    }
}