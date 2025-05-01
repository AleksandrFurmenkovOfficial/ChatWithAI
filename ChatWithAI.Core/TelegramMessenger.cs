using RxTelegram.Bot;
using RxTelegram.Bot.Interface.BaseTypes;
using RxTelegram.Bot.Interface.BaseTypes.Enums;
using RxTelegram.Bot.Interface.BaseTypes.Requests.Attachments;
using RxTelegram.Bot.Interface.BaseTypes.Requests.Messages;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace ChatWithAI.Core
{
    public sealed class TelegramMessenger(
        TelegramConfig config,
        ConcurrentDictionary<string, ConcurrentDictionary<string, ActionId>> actionsMapping,
        IMessengerBotSource telegramBotSource) : IMessenger
    {
        private ITelegramBot Bot => (ITelegramBot)telegramBotSource.Bot();
        private const ParseMode DefaultParseMode = ParseMode.HTML;

        private static ParseMode GetParseMode(int i)
        {
            return i switch
            {
                (int)ParseMode.HTML => ParseMode.HTML,
                (int)ParseMode.Markdown => ParseMode.Markdown,
                (int)ParseMode.MarkdownV2 => ParseMode.MarkdownV2,
                _ => DefaultParseMode,
            };
        }

        private static readonly TimeSpan[] DelayPolicy = [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200)];

        private InlineKeyboardMarkup? GetInlineKeyboardMarkup(string chatId, IEnumerable<ActionId>? messageActionIds)
        {
            var messageActionIdsList = messageActionIds?.ToList();
            if (messageActionIdsList == null || messageActionIdsList.Count == 0)
            {
                return null;
            }

            var mapping = actionsMapping.GetOrAdd(chatId, _ => new ConcurrentDictionary<string, ActionId>());
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

        public Task<bool> DeleteMessage(string chatId, MessageId messageId,
            CancellationToken cancellationToken = default)
        {
            var deleteMessageRequest = new DeleteMessage
            {
                ChatId = Helpers.StrToLong(chatId),
                MessageId = Helpers.MessageIdToInt(messageId)
            };
            return Bot.DeleteMessage(deleteMessageRequest, cancellationToken);
        }

        public async Task<string> SendTextMessage(string chatId, ChatMessage message,
            IEnumerable<ActionId>? messageActionIds = null, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithRetryAsync(async (parseMode) =>
            {
                var textContent = message.Content.OfType<TextContentItem>().FirstOrDefault() ?? throw new InvalidOperationException("Message content does not contain TextContentItem.");
                var sendMessageRequest = new SendMessage
                {
                    ChatId = Helpers.StrToLong(chatId),
                    Text = textContent.Text,
                    ReplyMarkup = GetInlineKeyboardMarkup(chatId, messageActionIds),
                    ParseMode = parseMode
                };
                var sentMessage = await Bot.SendMessage(sendMessageRequest, cancellationToken).ConfigureAwait(false);
                message.IsSent = true;
                return sentMessage.MessageId.ToString(CultureInfo.InvariantCulture);
            }, DelayPolicy, cancellationToken).ConfigureAwait(false);
        }

        public async Task EditTextMessage(string chatId, MessageId messageId, string content,
            IEnumerable<ActionId>? messageActionIds = null, CancellationToken cancellationToken = default)
        {
            await ExecuteWithRetryAsync(async (parseMode) =>
            {
                var editMessageRequest = new EditMessageText
                {
                    ChatId = Helpers.StrToLong(chatId),
                    MessageId = Helpers.MessageIdToInt(messageId),
                    Text = content,
                    ReplyMarkup = GetInlineKeyboardMarkup(chatId, messageActionIds),
                    ParseMode = parseMode
                };

                await Bot.EditMessageText(editMessageRequest, cancellationToken).ConfigureAwait(false);
                return true;
            }, DelayPolicy, cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> SendPhotoMessage(string chatId, ChatMessage message,
            IEnumerable<ActionId>? messageActionIds = null, CancellationToken cancellationToken = default)
        {
            return await ExecuteWithRetryAsync(async (parseMode) =>
            {
                var captionContent = message.Content.OfType<TextContentItem>().FirstOrDefault() ?? throw new InvalidOperationException("Message content does not contain TextContentItem.");
                var caption = captionContent.Text;

                try
                {
                    var imageContent = message.Content.OfType<ImageContentItem>().FirstOrDefault() ?? throw new InvalidOperationException("Message content does not contain ImageContentItem.");
                    Stream imageStream = imageContent.ImageInBase64 != null && imageContent.ImageInBase64.Length > 0
                        ? Helpers.ConvertBase64ToMemoryStream(imageContent.ImageInBase64)
                        : await Helpers.GetStreamFromUrlAsync(imageContent.ImageUrl!, cancellationToken).ConfigureAwait(false);

                    await using (imageStream)
                    {
                        // TODO: presave full size tmp solution
                        if (DefaultParseMode == parseMode) // to skip retries
                        {
                            var sendPhotoFileMessage = new SendDocument
                            {
                                ChatId = Helpers.StrToLong(chatId),
                                Document = new InputFile(imageStream, "full_size.webp"),
                                ReplyMarkup = GetInlineKeyboardMarkup(chatId, []),
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
                            Caption = caption,
                            ReplyMarkup = GetInlineKeyboardMarkup(chatId, messageActionIds),
                            ParseMode = parseMode
                        };

                        var result = await Bot.SendPhoto(sendPhotoMessage, cancellationToken).ConfigureAwait(false);
                        message.IsSent = true;
                        return result.MessageId.ToString(CultureInfo.InvariantCulture);
                    }
                }
                catch
                {
                    Debug.WriteLine(caption);
                    throw;
                }
            }, DelayPolicy, cancellationToken).ConfigureAwait(false);
        }

        public async Task EditPhotoMessage(string chatId, MessageId messageId, string caption,
            IEnumerable<ActionId>? messageActionIds = null, CancellationToken cancellationToken = default)
        {
            await ExecuteWithRetryAsync(async (parseMode) =>
            {
                var editCaptionRequest = new EditMessageCaption
                {
                    ChatId = Helpers.StrToLong(chatId),
                    MessageId = Helpers.MessageIdToInt(messageId),
                    Caption = caption,
                    ReplyMarkup = GetInlineKeyboardMarkup(chatId, messageActionIds),
                    ParseMode = parseMode
                };

                await Bot.EditMessageCaption(editCaptionRequest, cancellationToken).ConfigureAwait(false);
                return true;
            }, DelayPolicy, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<T> ExecuteWithRetryAsync<T>(Func<ParseMode, Task<T>> operation, TimeSpan[] retryDelays, CancellationToken cancellationToken)
        {
            int tryCount = 2;
            while (true)
            {
                try
                {
                    return await operation(GetParseMode(tryCount)).ConfigureAwait(false);
                }
                catch
                {
                    if (tryCount == 0)
                    {
                        throw;
                    }
                    await Task.Delay(retryDelays[tryCount], cancellationToken).ConfigureAwait(false);
                    --tryCount;
                }
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
