using RxTelegram.Bot;
using RxTelegram.Bot.Interface.BaseTypes;
using RxTelegram.Bot.Interface.BaseTypes.Enums;
using RxTelegram.Bot.Interface.BaseTypes.Requests.Attachments;
using RxTelegram.Bot.Interface.BaseTypes.Requests.Messages;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ChatWithAI.Core
{
    public sealed class TelegramMessenger(
        TelegramConfig config,
        ConcurrentDictionary<string, ConcurrentDictionary<string, ActionId>> actionsMappingByChat,
        IMessengerBotSource telegramBotSource) : IMessenger
    {
        private ITelegramBot Bot => (ITelegramBot)telegramBotSource.Bot();
        private const ParseMode DefaultParseMode = ParseMode.Markdown;

        private static readonly TimeSpan[] DelayPolicy = [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200)];

        private InlineKeyboardMarkup? GetNewInlineKeyboardMarkup(string chatId, IEnumerable<ActionId>? messageActionIds)
        {
            var messageActionIdsList = messageActionIds?.ToList();
            if (messageActionIdsList == null || messageActionIdsList.Count == 0)
            {
                return null;
            }

            var mapping = actionsMappingByChat.GetOrAdd(chatId, _ => new ConcurrentDictionary<string, ActionId>());
            mapping.Clear();

            var buttons = messageActionIdsList.Select(callbackId =>
            {
                var token = Guid.NewGuid().ToString();
                if (mapping.TryAdd(token, callbackId))
                {
                    return new InlineKeyboardButton
                    {
                        Text = callbackId.Name,
                        CallbackData = token
                    };
                }
                return null;
            })
            .Where(button => button != null)
            .ToArray();

            if (buttons.Length == 0)
                return null;

            return new InlineKeyboardMarkup
            {
                InlineKeyboard = new[] { buttons }
            };
        }

        public async Task<bool> DeleteMessage(string chatId, MessageId messageId,
            CancellationToken cancellationToken = default)
        {
            var deleteMessageRequest = new DeleteMessage
            {
                ChatId = Helpers.StrToLong(chatId),
                MessageId = Helpers.MessageIdToInt(messageId)
            };

            var result = await Bot.DeleteMessage(deleteMessageRequest, cancellationToken);
            actionsMappingByChat.Remove(chatId, out _);

            return result;
        }

        public async Task<string> SendTextMessage(string chatId, ChatMessage message,
            IEnumerable<ActionId>? messageActionIds = null, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithRetryAsync(async (parseMode) =>
            {
                var textContent = message.Content.OfType<TextContentItem>().FirstOrDefault() ?? throw new InvalidOperationException("Message content does not contain TextContentItem.");

                async Task<string> InternalReTry(int attempt = 0)
                {
                    string? currentText = attempt == 0 ? textContent.Text : removeFormatting(textContent.Text!, parseMode);

                    var sendMessageRequest = new SendMessage
                    {
                        ChatId = Helpers.StrToLong(chatId),
                        Text = currentText,
                        ReplyMarkup = GetNewInlineKeyboardMarkup(chatId, messageActionIds),
                        ParseMode = parseMode
                    };

                    try
                    {
                        var sentMessage = await Bot.SendMessage(sendMessageRequest, cancellationToken).ConfigureAwait(false);
                        message.IsSent = true;
                        message.IsActive = true;
                        return sentMessage.MessageId.ToString(CultureInfo.InvariantCulture);
                    }
                    catch when (attempt < 1)
                    {
                        return await InternalReTry(attempt + 1);
                    }
                }

                return await InternalReTry().ConfigureAwait(false);

            }, DelayPolicy, cancellationToken).ConfigureAwait(false);
        }

        public async Task EditTextMessage(string chatId, MessageId messageId, string content,
            IEnumerable<ActionId>? messageActionIds = null, CancellationToken cancellationToken = default)
        {
            await ExecuteWithRetryAsync(async (parseMode) =>
            {
                async Task<bool> InternalReTry(int attempt = 0)
                {
                    string currentContent = attempt == 0 ? content : removeFormatting(content, parseMode);

                    var editMessageRequest = new EditMessageText
                    {
                        ChatId = Helpers.StrToLong(chatId),
                        MessageId = Helpers.MessageIdToInt(messageId),
                        Text = currentContent,
                        ReplyMarkup = GetNewInlineKeyboardMarkup(chatId, messageActionIds),
                        ParseMode = parseMode
                    };

                    try
                    {
                        await Bot.EditMessageText(editMessageRequest, cancellationToken).ConfigureAwait(false);
                        return true;
                    }
                    catch when (attempt < 1)
                    {
                        return await InternalReTry(attempt + 1);
                    }
                }

                return await InternalReTry().ConfigureAwait(false);

            }, DelayPolicy, cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> SendPhotoMessage(string chatId, ChatMessage message,
            IEnumerable<ActionId>? messageActionIds = null, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithRetryAsync(async (parseMode) =>
            {
                var captionContent = message.Content.OfType<TextContentItem>().FirstOrDefault() ?? throw new InvalidOperationException("Message content does not contain TextContentItem.");

                async Task<string> InternalReTry(int attempt = 0)
                {
                    string? currentCaption = attempt == 0 ? captionContent.Text : removeFormatting(captionContent.Text!, parseMode);

                    var imageContent = message.Content.OfType<ImageContentItem>().FirstOrDefault() ?? throw new InvalidOperationException("Message content does not contain ImageContentItem.");
                    Stream imageStream = imageContent.ImageInBase64 != null && imageContent.ImageInBase64.Length > 0
                        ? Helpers.ConvertBase64ToMemoryStream(imageContent.ImageInBase64)
                        : await Helpers.GetStreamFromUrlAsync(imageContent.ImageUrl!, cancellationToken).ConfigureAwait(false);

                    await using (imageStream.ConfigureAwait(false))
                    {
                        if (DefaultParseMode == parseMode) // to skip retries
                        {
                            var sendPhotoFileMessage = new SendDocument
                            {
                                ChatId = Helpers.StrToLong(chatId),
                                Document = new InputFile(imageStream, "full_size.webp"),
                                ReplyMarkup = GetNewInlineKeyboardMarkup(chatId, []),
                                ParseMode = parseMode,
                                DisableContentTypeDetection = true,
                                ProtectContent = false
                            };

                            await Bot.SendDocument(sendPhotoFileMessage, cancellationToken).ConfigureAwait(false);
                        }

                        imageStream.Position = 0;
                        var sendPhotoMessage = new SendPhoto
                        {
                            ChatId = Helpers.StrToLong(chatId),
                            Photo = new InputFile(imageStream),
                            Caption = currentCaption,
                            ReplyMarkup = GetNewInlineKeyboardMarkup(chatId, messageActionIds),
                            ParseMode = parseMode
                        };

                        try
                        {
                            var result = await Bot.SendPhoto(sendPhotoMessage, cancellationToken).ConfigureAwait(false);
                            message.IsSent = true;
                            message.IsActive = true;
                            return result.MessageId.ToString(CultureInfo.InvariantCulture);
                        }
                        catch when (attempt < 1)
                        {
                            return await InternalReTry(attempt + 1);
                        }
                    }
                }

                return await InternalReTry().ConfigureAwait(false);

            }, DelayPolicy, cancellationToken).ConfigureAwait(false);
        }

        public async Task EditPhotoMessage(string chatId, MessageId messageId, string caption,
            IEnumerable<ActionId>? messageActionIds = null, CancellationToken cancellationToken = default)
        {
            await ExecuteWithRetryAsync(async (parseMode) =>
            {
                async Task<bool> InternalReTry(int attempt = 0)
                {
                    string currentCaption = attempt == 0 ? caption : removeFormatting(caption, parseMode);

                    var editCaptionRequest = new EditMessageCaption
                    {
                        ChatId = Helpers.StrToLong(chatId),
                        MessageId = Helpers.MessageIdToInt(messageId),
                        Caption = currentCaption,
                        ReplyMarkup = GetNewInlineKeyboardMarkup(chatId, messageActionIds),
                        ParseMode = parseMode
                    };

                    try
                    {
                        await Bot.EditMessageCaption(editCaptionRequest, cancellationToken).ConfigureAwait(false);
                        return true;
                    }
                    catch when (attempt < 1)
                    {
                        return await InternalReTry(attempt + 1);
                    }
                }

                return await InternalReTry().ConfigureAwait(false);

            }, DelayPolicy, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<T> ExecuteWithRetryAsync<T>(Func<ParseMode, Task<T>> operation, TimeSpan[] retryDelays, CancellationToken cancellationToken)
        {
            int tryCount = 0;
            while (true)
            {
                try
                {
                    if (tryCount == 0)
                    {
                        return await operation(ParseMode.Markdown).ConfigureAwait(false);
                    }
                    else if (tryCount == 1)
                    {
                        return await operation(ParseMode.HTML).ConfigureAwait(false);
                    }
                }
                catch when (tryCount < 2)
                {
                    await Task.Delay(retryDelays[tryCount], cancellationToken).ConfigureAwait(false);
                    ++tryCount;
                }
            }
        }

        private static string removeFormatting(string text, ParseMode parseMode)
        {
            switch (parseMode)
            {
                case ParseMode.Markdown:
                    return text.Replace("**", "").Replace('*', '•').Replace('_', ' ').Replace('`', '\'').Trim();
                case ParseMode.HTML:
                    return Regex.Replace(text, @"<[^>]*>", string.Empty);
                default:
                    return text;
            }
        }

        public int MaxTextMessageLen()
        {
            return config.MessageLengthLimit;
        }

        public int MaxPhotoMessageLen()
        {
            return config.PhotoMessageLengthLimit;
        }
    }
}