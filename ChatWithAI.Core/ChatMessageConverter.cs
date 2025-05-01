using ChatWithAI.Core;
using RxTelegram.Bot;
using RxTelegram.Bot.Interface.BaseTypes;
using System.Text;
using System.Text.RegularExpressions;


namespace TelegramChatGPT.Implementation
{
    public sealed partial class ChatMessageConverter(
        string telegramBotKey,
        IMessengerBotSource botSource) : IChatMessageConverter
    {
        private const string TelegramBotFile = "https://api.telegram.org/file/bot";
        private static readonly Regex WrongNameSymbolsRegExp = WrongNameSymbolsRegexpCreator();
        private ITelegramBot Bot => (ITelegramBot)botSource.Bot();

        public async Task<ChatMessage> ConvertToChatMessage(object rawMessage, CancellationToken cancellationToken = default)
        {
            if (rawMessage is not Message castedMessage)
            {
                throw new InvalidCastException(nameof(rawMessage));
            }

            List<ContentItem> content = [];
            string forwardedFrom = "";
            string forwardedMessageContent = string.Empty;

            if (castedMessage.ForwardOrigin != null)
            {
                switch (castedMessage.ForwardOrigin)
                {
                    case MessageOriginChannel messageOriginChannel:
                        forwardedFrom = $"Channel \"{messageOriginChannel.Chat.Title}\"(@{messageOriginChannel.Chat.Username})";
                        break;

                    case MessageOriginChat messageOriginChat:
                        forwardedFrom = $"Chat \"{messageOriginChat.SenderChat.Title}\"(@{messageOriginChat.SenderChat.Username})";
                        break;

                    case MessageOriginHiddenUser messageOriginHiddenUser:
                        forwardedFrom = $"Hidden user \"{messageOriginHiddenUser.SenderUserName}\"";
                        break;

                    case MessageOriginUser messageOriginUser:
                        forwardedFrom = $"User \"{CompoundUserName(messageOriginUser.SenderUser)}\"";
                        break;
                }

                string replyToPhotoLink = castedMessage.ReplyToMessage?.Photo != null
                    ? await PhotoToLink(castedMessage.ReplyToMessage.Photo, cancellationToken).ConfigureAwait(false)
                    : string.Empty;

                var replyTo = castedMessage!.ReplyToMessage?.Text ?? castedMessage!.ReplyToMessage?.Caption;
                if (!string.IsNullOrEmpty(replyTo))
                {
                    forwardedMessageContent = replyTo;
                }

                if (!string.IsNullOrEmpty(replyToPhotoLink))
                {
                    var uri = new Uri(replyToPhotoLink);
                    string image64 = await Helpers.EncodeImageToWebpBase64(uri, cancellationToken).ConfigureAwait(false);
                    content.Add(ChatMessage.CreateImage(uri, image64));
                }
            }

            if (castedMessage.Sticker?.Thumbnail != null)
            {
                var link = await PhotoToLink([castedMessage.Sticker.Thumbnail], cancellationToken).ConfigureAwait(false);
                var uri = new Uri(link);
                string image64 = await Helpers.EncodeImageToWebpBase64(uri, cancellationToken).ConfigureAwait(false);
                content.Add(ChatMessage.CreateImage(uri, image64));
            }

            string userPhotoLink = castedMessage!.Photo != null
                ? await PhotoToLink(castedMessage.Photo, cancellationToken).ConfigureAwait(false)
                : string.Empty;

            if (!string.IsNullOrEmpty(userPhotoLink))
            {
                var uri = new Uri(userPhotoLink);
                string image64 = await Helpers.EncodeImageToWebpBase64(uri, cancellationToken).ConfigureAwait(false);
                content.Add(ChatMessage.CreateImage(uri, image64));
            }

            string userContent = (castedMessage!.Text ?? castedMessage!.Caption ?? string.Empty);

            var fromUser = CompoundUserName(castedMessage.From);
            forwardedFrom = string.IsNullOrEmpty(forwardedFrom) ? "" : "\nUser \"" + fromUser + "\" forwarded message from " + forwardedFrom + ".";
            if (!string.IsNullOrEmpty(forwardedFrom) && string.IsNullOrEmpty(forwardedMessageContent))
            {
                forwardedMessageContent = userContent;
                userContent = "";
            }

            if (!string.IsNullOrEmpty(forwardedMessageContent))
            {
                forwardedMessageContent = "\nForwardedContent: \"" + forwardedMessageContent + "\"";
            }

            var combinedContent = userContent + forwardedFrom + forwardedMessageContent;
            if (string.IsNullOrEmpty(combinedContent) && castedMessage.Sticker != null)
            {
                combinedContent = $"<system_message>User sent a sticker. The sticker is associated with the emoji {castedMessage.Sticker.Emoji}. Please respond as if the user had sent this sticker directly.</system_message>";
            }

            content.Add(ChatMessage.CreateText(combinedContent));

            if (castedMessage.Audio != null)
            {
                var link = await AudioToLink(castedMessage.Audio, cancellationToken).ConfigureAwait(false);
                var uri = new Uri(link);
                string audio = await Helpers.EncodeAudioToWebpBase64(uri, cancellationToken).ConfigureAwait(false);
                content.Add(ChatMessage.CreateAudio(uri, audio));
            }

            var resultMessage = new ChatMessage
            {
                Id = new MessageId(castedMessage!.MessageId.ToString(CultureInfo.InvariantCulture)),
                Name = fromUser,
                Role = MessageRole.eRoleUser,
                Content = content,
            };

            return resultMessage;
        }

        private static string CompoundUserName(User user)
        {
            string input = RemoveWrongSymbols($"{user.FirstName}_{user.LastName}_{user.Username}");
            var result = WrongNameSymbolsRegExp.Replace(input, string.Empty).Replace(' ', '_').TrimStart('_')
                .TrimEnd('_');
            if (result.Replace("_", string.Empty, StringComparison.InvariantCultureIgnoreCase).Length == 0)
            {
                result = $"User{user.Id}";
            }

            return result.Replace("__", "_", StringComparison.InvariantCultureIgnoreCase);

            static string RemoveWrongSymbols(string input)
            {
                var cleanText = new StringBuilder();
                foreach (var ch in input.Where(ch =>
                             char.IsAscii(ch) && !char.IsSurrogate(ch) && !IsEmoji(ch) &&
                             (ch == '_' || char.IsAsciiLetterOrDigit(ch))))
                {
                    cleanText.Append(ch);
                }

                return cleanText.ToString();

                static bool IsEmoji(char ch)
                {
                    var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(ch);
                    return unicodeCategory == UnicodeCategory.OtherSymbol;
                }
            }
        }

        private async Task<string> PhotoToLink(IEnumerable<PhotoSize> photos,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return await Task.FromCanceled<string>(cancellationToken).ConfigureAwait(false);
            }

            var photoSize = photos.Last();
            var file = await Bot.GetFile(photoSize.FileId, cancellationToken).ConfigureAwait(false);
            return $"{new Uri(new Uri($"{TelegramBotFile}{telegramBotKey}/"), file.FilePath)}";
        }

        private async Task<string> AudioToLink(Audio audio,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return await Task.FromCanceled<string>(cancellationToken).ConfigureAwait(false);
            }

            var file = await Bot.GetFile(audio.FileId, cancellationToken).ConfigureAwait(false);
            return $"{new Uri(new Uri($"{TelegramBotFile}{telegramBotKey}/"), file.FilePath)}";
        }

        [GeneratedRegex("[^a-zA-Z0-9_\\s-]")]
        private static partial Regex WrongNameSymbolsRegexpCreator();
    }
}