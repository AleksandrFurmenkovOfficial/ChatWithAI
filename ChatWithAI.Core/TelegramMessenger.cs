using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using MessageId = ChatWithAI.Contracts.MessageId;

namespace ChatWithAI.Core
{
    public sealed partial class TelegramMessenger(
        TelegramConfig config,
        ConcurrentDictionary<string, ConcurrentDictionary<string, ActionId>> actionsMappingByChat,
        IMessengerBotSource telegramBotSource,
        IContentHelper contentHelper) : IMessenger
    {
        private ITelegramBotClient Bot => telegramBotSource.Bot() as ITelegramBotClient
            ?? throw new InvalidOperationException("Bot client is null or invalid type");

        private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(200);
        private static readonly ParseMode DefaultParseMode = ParseMode.Html;
        private static readonly int TimeoutInMs = 60000;

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
                    return InlineKeyboardButton.WithCallbackData(callbackId.Name, token);
                }
                return null;
            })
            .Where(button => button != null)
            .Select(b => b!)
            .ToArray();

            if (buttons.Length == 0)
                return null;

            return new InlineKeyboardMarkup([buttons]);
        }

        public async Task<bool> DeleteMessage(string chatId, MessageId messageId)
        {
            long chatIdLong = Helpers.StrToLong(chatId);
            int msgIdInt = Helpers.MessageIdToInt(messageId);

            async Task<bool> InternalReTry(int attempt = 0)
            {
                try
                {
                    using CancellationTokenSource cts = new(TimeoutInMs);
                    await Bot.DeleteMessage(chatIdLong, msgIdInt, cancellationToken: cts.Token)
                        .ConfigureAwait(false);

                    actionsMappingByChat.TryRemove(chatId, out _);

                    return true;
                }
                catch when (attempt < 2)
                {
                    await Task.Delay(Delay).ConfigureAwait(false);
                    return await InternalReTry(attempt + 1).ConfigureAwait(false);
                }
            }

            return await InternalReTry().ConfigureAwait(false);
        }

        public async Task<string> SendTextMessage(string chatId, MessengerMessageDTO message, IEnumerable<ActionId>? messageActionIds = null)
        {
            long chatIdLong = Helpers.StrToLong(chatId);

            return await ExecuteWithRetryAsync(async (parseMode) =>
            {
                if (string.IsNullOrEmpty(message.TextContent))
                    throw new InvalidOperationException("Message text content is empty.");

                async Task<string> InternalReTry(int attempt = 0)
                {
                    string targetText = FixFormatting(message.TextContent, parseMode);

                    try
                    {
                        using CancellationTokenSource cts = new(TimeoutInMs);

                        var sentMessage = await Bot.SendMessage(
                            chatId: chatIdLong,
                            text: targetText,
                            parseMode: parseMode,
                            replyMarkup: GetNewInlineKeyboardMarkup(chatId, messageActionIds),
                            cancellationToken: cts.Token
                        ).ConfigureAwait(false);

                        return sentMessage.MessageId.ToString(CultureInfo.InvariantCulture);
                    }
                    catch when (attempt < 1)
                    {
                        await Task.Delay(Delay).ConfigureAwait(false);
                        return await InternalReTry(attempt + 1).ConfigureAwait(false);
                    }
                }

                return await InternalReTry().ConfigureAwait(false);

            }).ConfigureAwait(false);
        }

        public async Task<MessengerEditResult> EditTextMessage(string chatId, MessageId messageId, string content,
            IEnumerable<ActionId>? messageActionIds = null)
        {
            long chatIdLong = Helpers.StrToLong(chatId);
            int msgIdInt = Helpers.MessageIdToInt(messageId);

            return await ExecuteEditWithRetryAsync(async (parseMode) =>
            {
                string targetText = FixFormatting(content, parseMode);
                using CancellationTokenSource cts = new(TimeoutInMs);

                await Bot.EditMessageText(
                    chatId: chatIdLong,
                    messageId: msgIdInt,
                    text: targetText,
                    parseMode: parseMode,
                    replyMarkup: GetNewInlineKeyboardMarkup(chatId, messageActionIds),
                    cancellationToken: cts.Token
                ).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        public async Task<string> SendPhotoMessage(string chatId, MessengerMessageDTO message,
            IEnumerable<ActionId>? messageActionIds = null)
        {
            long chatIdLong = Helpers.StrToLong(chatId);

            var imageContent = message.MediaContent.OfType<ImageContentItem>().FirstOrDefault()
                ?? throw new InvalidOperationException("Message content does not contain ImageContentItem.");

            byte[] imageBytes;
            var imageBase64 = await imageContent.GetImageBase64Async().ConfigureAwait(false);
            Stream? originalStream = !string.IsNullOrEmpty(imageBase64)
                ? Helpers.ConvertBase64ToMemoryStream(imageBase64)
                : await contentHelper.GetStreamFromUrlAsync(imageContent.ImageUrl!).ConfigureAwait(false);

            if (originalStream == null)
            {
                throw new InvalidOperationException("Failed to retrieve image from URL.");
            }

            using (originalStream)
            {
                if (originalStream is MemoryStream ms && ms.TryGetBuffer(out var segment))
                {
                    imageBytes = segment.ToArray();
                }
                else
                {
                    using var copy = new MemoryStream();
                    await originalStream.CopyToAsync(copy).ConfigureAwait(false);
                    imageBytes = copy.ToArray();
                }
            }

            return await ExecuteWithRetryAsync(async (parseMode) =>
            {
                async Task<string> InternalReTry(int attempt = 0)
                {
                    string targetText = FixFormatting(message.TextContent, parseMode);

                    try
                    {
                        using CancellationTokenSource cts = new(TimeoutInMs);

                        if (DefaultParseMode == parseMode && attempt == 0)
                        {
                            try
                            {
                                using var docStream = new MemoryStream(imageBytes);
                                var inputFile = InputFile.FromStream(docStream, "full_size.webp");

                                await Bot.SendDocument(
                                    chatId: chatIdLong,
                                    document: inputFile,
                                    replyMarkup: GetNewInlineKeyboardMarkup(chatId, []),
                                    parseMode: parseMode,
                                    disableContentTypeDetection: true,
                                    cancellationToken: cts.Token
                                ).ConfigureAwait(false);
                            }
                            catch
                            {
                            }
                        }

                        using var photoStream = new MemoryStream(imageBytes);
                        var photoFile = InputFile.FromStream(photoStream, "image.webp");

                        var sentMessage = await Bot.SendPhoto(
                            chatId: chatIdLong,
                            photo: photoFile,
                            caption: targetText,
                            parseMode: parseMode,
                            replyMarkup: GetNewInlineKeyboardMarkup(chatId, messageActionIds),
                            cancellationToken: cts.Token
                        ).ConfigureAwait(false);

                        return sentMessage.MessageId.ToString(CultureInfo.InvariantCulture);
                    }
                    catch when (attempt < 1)
                    {
                        await Task.Delay(Delay).ConfigureAwait(false);
                        return await InternalReTry(attempt + 1).ConfigureAwait(false);
                    }
                }

                return await InternalReTry().ConfigureAwait(false);

            }).ConfigureAwait(false);
        }

        public async Task<MessengerEditResult> EditPhotoMessage(string chatId, MessageId messageId, string caption,
            IEnumerable<ActionId>? messageActionIds = null)
        {
            long chatIdLong = Helpers.StrToLong(chatId);
            int msgIdInt = Helpers.MessageIdToInt(messageId);

            return await ExecuteEditWithRetryAsync(async (parseMode) =>
            {
                string targetText = FixFormatting(caption, parseMode);
                using CancellationTokenSource cts = new(TimeoutInMs);

                await Bot.EditMessageCaption(
                    chatId: chatIdLong,
                    messageId: msgIdInt,
                    caption: targetText,
                    parseMode: parseMode,
                    replyMarkup: GetNewInlineKeyboardMarkup(chatId, messageActionIds),
                    cancellationToken: cts.Token
                ).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        private static async Task<T> ExecuteWithRetryAsync<T>(Func<ParseMode, Task<T>> operation)
        {
            var modes = new[]
            {
                ParseMode.Html,
                ParseMode.Markdown,
                ParseMode.None
            };

            for (int i = 0; i < modes.Length; i++)
            {
                try
                {
                    return await operation(modes[i]).ConfigureAwait(false);
                }
                catch (Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.ErrorCode == 400)
                {
                    if (i == modes.Length - 1) throw;
                    await Task.Delay(Delay).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("Unreachable code");
        }

        private static async Task<MessengerEditResult> ExecuteEditWithRetryAsync(Func<ParseMode, Task> operation)
        {
            var modes = new[]
            {
                ParseMode.Html,
                ParseMode.Markdown,
                ParseMode.None
            };

            for (int i = 0; i < modes.Length; i++)
            {
                try
                {
                    await operation(modes[i]).ConfigureAwait(false);
                    return MessengerEditResult.Success;
                }
                catch (Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.ErrorCode == 400)
                {
                    // Message was deleted by user - don't retry, return immediately
                    if (ex.Message.Contains("message to edit not found", StringComparison.OrdinalIgnoreCase) ||
                        ex.Message.Contains("message to delete not found", StringComparison.OrdinalIgnoreCase))
                    {
                        return MessengerEditResult.MessageDeleted;
                    }

                    // Message content is the same - not an error
                    if (ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
                    {
                        return MessengerEditResult.NotModified;
                    }

                    // Other 400 errors - try different parse mode
                    if (i == modes.Length - 1) throw;
                    await Task.Delay(Delay).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("Unreachable code");
        }

        private static string FixFormatting(string text, ParseMode parseMode)
        {
            if (string.IsNullOrEmpty(text)) return " ";

            var data = parseMode switch
            {
                ParseMode.Html => TelegramFormatHelper.ConvertToTelegramHtml(text),
                _ => text,
            };

            return string.IsNullOrEmpty(data) ? " " : data;
        }

        public int MaxTextMessageLen()
        {
            return config.MessageLengthLimit - 512; // 512 is for tags
        }

        public int MaxPhotoMessageLen()
        {
            return config.PhotoMessageLengthLimit - 256; // 256 is for tags
        }

        [GeneratedRegex(@"<[^>]*>")]
        private static partial Regex HtmlRegex();
    }
}