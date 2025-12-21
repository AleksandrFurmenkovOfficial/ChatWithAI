using ChatWithAI.Contracts.Model;
using System.Collections.Concurrent;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

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
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ActionId>> actionsMappingByChat;

        private int disposed;
        private readonly ILogger? logger;
        private readonly CancellationTokenSource cancellationTokenSource = new();

        private ITelegramBotClient? bot;
        private readonly IMessengerBotSource telegramBotSource;
        private readonly ChatCache cache;

        // Subscription subjects
        private readonly CompositeDisposable subscriptions = [];

        private readonly Subject<EventChatAction> chatActionSubject = new();
        public IObservable<EventChatAction> ChatActions => chatActionSubject.AsObservable();

        private readonly Subject<EventChatMessage> chatMessageSubject = new();
        public IObservable<EventChatMessage> ChatMessages => chatMessageSubject.AsObservable();

        private readonly Subject<EventChatCommand> chatCommandSubject = new();
        public IObservable<EventChatCommand> ChatCommands => chatCommandSubject.AsObservable();

        private readonly Subject<EventChatExpire> chatExpireSubject = new();
        public IObservable<EventChatExpire> ExpireChats => chatExpireSubject.AsObservable();

        private readonly Subject<CallbackQuery> callbackQuerySubject = new();
        private readonly Subject<Message> messageSubject = new();

        public ChatEventSource(
            List<IChatCommand> commands,
            ConcurrentDictionary<string, ConcurrentDictionary<string, ActionId>> actionsMappingByChat,
            IMessengerBotSource telegramBotSource,
            IChatMessageConverter chatMessageConverter,
            IAdminChecker adminChecker,
            ChatCache cache,
            ILogger logger)
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
            bool hasDocument = IsPdfDocument(message.Document) || IsPdfDocument(message.ReplyToMessage?.Document);
            bool hasVideo = message.Video != null || message.VideoNote != null || message.Animation != null ||
                            message.ReplyToMessage?.Video != null || message.ReplyToMessage?.VideoNote != null || message.ReplyToMessage?.Animation != null ||
                            IsVideoDocument(message.Document) || IsVideoDocument(message.ReplyToMessage?.Document);
            //bool hasAnimation = message.Animation != null;
            return hasText || hasPhoto || hasAudio || hasDocument || hasVideo; // || hasAnimation
        }

        private static bool IsPdfDocument(Document? document)
        {
            if (document == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(document.MimeType))
            {
                return string.Equals(document.MimeType, "application/pdf", StringComparison.OrdinalIgnoreCase);
            }

            return document.FileName?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static bool IsVideoDocument(Document? document)
        {
            if (document == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(document.MimeType))
            {
                return document.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
            }

            if (string.IsNullOrWhiteSpace(document.FileName))
            {
                return false;
            }

            return document.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                   || document.FileName.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)
                   || document.FileName.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)
                   || document.FileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
                   || document.FileName.EndsWith(".avi", StringComparison.OrdinalIgnoreCase)
                   || document.FileName.EndsWith(".mpeg", StringComparison.OrdinalIgnoreCase)
                   || document.FileName.EndsWith(".mpg", StringComparison.OrdinalIgnoreCase);
        }

        public Task Run()
        {
            return InitializeBot();
        }

        private async Task InitializeBot()
        {
            if (disposed != 0) return;
            await telegramBotSource.NewBotAsync().ConfigureAwait(false);
            if (disposed != 0) return;

            bot = telegramBotSource.Bot() as ITelegramBotClient;
            if (bot == null)
            {
                throw new ArgumentNullException("The bot is not initialized.");
            }

            subscriptions.Clear();

            // Setup Rx pipeline for CallbackQueries
            var callbackSubscription = callbackQuerySubject
                .Where(IsValidCallbackQuery)
                .Buffer(TimeSpan.FromMilliseconds(50), 10)
                .Where(buffer => buffer.Count > 0)
                .SelectMany(buffer => buffer
                    .OrderBy(cq => cq.Id)
                    .GroupBy(cq => cq.From!.Id.ToString(CultureInfo.InvariantCulture))
                    .Select(chatGroup => chatGroup.Last())
                    .Select(callbackQuery =>
                    {
                        if (callbackQuery.Data == null)
                            return null;

                        var chatId = callbackQuery.From.Id.ToString(CultureInfo.InvariantCulture);
                        if (!actionsMappingByChat.TryGetValue(chatId, out var chatActions) ||
                            !chatActions.TryGetValue(callbackQuery.Data, out var actionId))
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

            // Setup Rx pipeline for Messages
            var messageSubscription = messageSubject
                .Where(IsValidMessage)
                .Buffer(TimeSpan.FromMilliseconds(200), 100)
                .Where(buffer => buffer.Count > 0)
                .SelectMany(buffer => buffer
                    .OrderBy(m => m.MessageId)
                    .Select(message => Observable.FromAsync(async ct =>
                    {
                        if (message.From == null)
                            return null;

                        var username = string.Join("_", message.From.FirstName, message.From.Username, message.From.LastName).Trim('_');
                        var chatMessage = await chatMessageConverter.ConvertToChatMessage(message, ct).ConfigureAwait(false);
                        if (chatMessage.Content.Count == 0)
                            return null;

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

            // Setup cache expiration subscription
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
                            logger?.LogDebugMessage($"Error processing cache expiration event: {ex.Message}");
                        }
                    },
                    error => HandlePipelineError(error, "Cache expiration pipeline")
                );
            subscriptions.Add(cacheExpirationSubscription);

            // Start receiving updates using new API
            StartReceiving();
        }

        private void StartReceiving()
        {
            if (bot == null) return;

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery],
                DropPendingUpdates = true,
                Limit = 100
            };

            bot.StartReceiving(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: cancellationTokenSource.Token
            );

            logger?.LogDebugMessage("Telegram bot polling started.");
        }

        private Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                // Feed updates into Rx subjects
                if (update.CallbackQuery != null)
                {
                    callbackQuerySubject.OnNext(update.CallbackQuery);
                }

                if (update.Message != null)
                {
                    messageSubject.OnNext(update.Message);
                }
            }
            catch (Exception ex)
            {
                logger?.LogDebugMessage($"Error processing update {update.Id}: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            if (exception is OperationCanceledException)
            {
                logger?.LogDebugMessage("Polling cancelled.");
                return Task.CompletedTask;
            }

            logger?.LogDebugMessage($"Polling error: {exception.Message}{Environment.NewLine}{exception.StackTrace}");

            return Task.CompletedTask;
        }

        private void HandlePipelineError(Exception exception, string pipelineName)
        {
            if (exception is OperationCanceledException)
            {
                logger?.LogDebugMessage($"[{pipelineName}] Rx pipeline subscription cancelled.");
                return;
            }
            logger?.LogDebugMessage($"[{pipelineName}] Unhandled error in Rx pipeline: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
        }

        private EventChatCommand? GetChatCommand(string chatId, ChatMessageModel message, string username)
        {
            if (message.Content == null || message.Content.Count == 0)
            {
                return null;
            }

            var textItem = message.GetTextContentItems().FirstOrDefault();
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
                    message.Id.Value.ToString(),
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

            // Give some time for ongoing operations to complete
            Thread.Sleep(500);

            cancellationTokenSource.Dispose();

            callbackQuerySubject?.OnCompleted();
            callbackQuerySubject?.Dispose();

            messageSubject?.OnCompleted();
            messageSubject?.Dispose();

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
