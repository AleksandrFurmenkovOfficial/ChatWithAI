using ChatWithAI.Contracts.Model;
using ChatWithAI.Core;
using System.Text;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramChatGPT.Implementation
{
    public sealed partial class TelegramChatMessageConverter(
        string telegramBotKey,
        IMessengerBotSource botSource,
        IContentHelper contentHelper) : IChatMessageConverter
    {
        private const string TelegramBotFile = "https://api.telegram.org/file/bot";
        private static readonly Regex WrongNameSymbolsRegExp = WrongNameSymbolsRegexpCreator();
        private ITelegramBotClient Bot => botSource.Bot() as ITelegramBotClient ?? throw new InvalidOperationException("Bot is not initialized");

        public async Task<ChatMessageModel> ConvertToChatMessage(object rawMessage, CancellationToken cancellationToken = default)
        {
            if (rawMessage is not Message castedMessage)
            {
                throw new InvalidCastException(nameof(rawMessage));
            }

            List<ContentItem> content = [];
            var uniqueIds = new HashSet<string>();

            var photos = await GetPhotos(castedMessage, uniqueIds, cancellationToken).ConfigureAwait(false);
            content.AddRange(photos);

            var sticker = await GetSticker(castedMessage, uniqueIds, cancellationToken).ConfigureAwait(false);
            content.AddRange(sticker);

            var audio = await AddAudio(castedMessage, uniqueIds, cancellationToken).ConfigureAwait(false);
            content.AddRange(audio);

            var videos = await AddVideos(castedMessage, uniqueIds, cancellationToken).ConfigureAwait(false);
            content.AddRange(videos);

            //var documents = await AddDocuments(castedMessage, uniqueIds, cancellationToken).ConfigureAwait(false);
            //content.AddRange(documents);

            var fromUser = castedMessage!.From != null ? CompoundUserName(castedMessage.From) : "UnknownUser";
            var text = AddText(castedMessage, fromUser);
            content.AddRange(text);

            var resultMessage = BuildMessage(castedMessage.MessageId, content, fromUser, castedMessage.Date);
            return resultMessage;
        }

        private static ChatMessageModel BuildMessage(int messageId, List<ContentItem> content, string fromUser, DateTime date)
        {
            var resultMessage = new ChatMessageModel
            {
                Name = fromUser,
                Role = MessageRole.eRoleUser,
                OriginalMessageId = messageId,
                Content = content,
                CreatedAt = date
            };

            return resultMessage;
        }

        private static List<ContentItem> AddText(Message castedMessage, string fromUser)
        {
            List<ContentItem> content = [];

            var replyTo = castedMessage!.ReplyToMessage?.Text ?? castedMessage!.ReplyToMessage?.Caption;
            string forwardedMessageContent = string.Empty;
            if (!string.IsNullOrEmpty(replyTo))
            {
                forwardedMessageContent = replyTo;
            }

            MessageOrigin? origin = castedMessage.ForwardOrigin ?? castedMessage.ReplyToMessage?.ForwardOrigin;
            string forwardedFrom = GetForwardedFrom(origin);
            forwardedFrom = string.IsNullOrEmpty(forwardedFrom) ? "" : $"Message has forwarded (media) content from {forwardedFrom}";

            string userContent = (castedMessage!.Text ?? castedMessage!.Caption ?? string.Empty);
            if (!string.IsNullOrEmpty(forwardedFrom) && string.IsNullOrEmpty(forwardedMessageContent))
            {
                forwardedMessageContent = userContent;
                userContent = "";
            }

            if (!string.IsNullOrEmpty(forwardedMessageContent))
            {
                forwardedMessageContent = $"Forwarded content timestamp: \"{origin!.Date}\"\nForwarded content: \"{forwardedMessageContent}\"\n";
            }

            userContent = string.IsNullOrEmpty(userContent) ? $"Message sent by: \"{fromUser}\"\nTimestamp: \"{DateTime.UtcNow}\"" : $"Message sent by: \"{fromUser}\"\nTimestamp: \"{DateTime.UtcNow}\"\nMessage content: \"{userContent}\"\n";
            var combinedContent = $"<system_message>AI, just keep your AI personality in each answer.</system_message>\n{userContent}\n{forwardedFrom}\n{forwardedMessageContent}".Trim();
            if (!string.IsNullOrWhiteSpace(combinedContent))
            {
                content.Add(ChatMessageModel.CreateText(combinedContent));
            }

            return content;
        }

        private async Task<List<ContentItem>> AddAudio(Message castedMessage, HashSet<string> uniqueIds, CancellationToken cancellationToken)
        {
            List<ContentItem> content = [];

            if (castedMessage.Audio != null && TryAddUniqueId(GetUniqueId(castedMessage.Audio), uniqueIds))
            {
                var link = await MediaToLink(castedMessage.Audio, cancellationToken).ConfigureAwait(false);
                var uri = new Uri(link);
                content.Add(CreateLazyAudio(uri));
            }

            if (castedMessage.Voice != null && TryAddUniqueId(GetUniqueId(castedMessage.Voice), uniqueIds))
            {
                var link = await MediaToLink(castedMessage.Voice, cancellationToken).ConfigureAwait(false);
                var uri = new Uri(link);
                content.Add(CreateLazyAudio(uri));
            }

            return content;
        }

        private async Task<List<ContentItem>> AddVideos(Message castedMessage, HashSet<string> uniqueIds, CancellationToken cancellationToken)
        {
            List<ContentItem> content = [];

            if (castedMessage.ReplyToMessage != null)
            {
                await AddVideosFromMessage(castedMessage.ReplyToMessage, content, uniqueIds, cancellationToken).ConfigureAwait(false);
            }

            await AddVideosFromMessage(castedMessage, content, uniqueIds, cancellationToken).ConfigureAwait(false);

            return content;
        }

        private async Task AddVideosFromMessage(Message message, List<ContentItem> content, HashSet<string> uniqueIds, CancellationToken cancellationToken)
        {
            //if (message.Video != null && TryAddUniqueId(GetUniqueId(message.Video), uniqueIds))
            //{
            //    var link = await MediaToLink(message.Video, cancellationToken).ConfigureAwait(false);
            //    var uri = new Uri(link);
            //    content.Add(CreateLazyVideo(uri, message.Video.MimeType));
            //}

            if (message.VideoNote != null && TryAddUniqueId(GetUniqueId(message.VideoNote), uniqueIds))
            {
                var link = await MediaToLink(message.VideoNote, cancellationToken).ConfigureAwait(false);
                var uri = new Uri(link);
                content.Add(CreateLazyVideo(uri, null));
            }

            if (message.Animation != null && TryAddUniqueId(GetUniqueId(message.Animation), uniqueIds))
            {
                var link = await MediaToLink(message.Animation, cancellationToken).ConfigureAwait(false);
                var uri = new Uri(link);
                content.Add(CreateLazyVideo(uri, message.Animation.MimeType));
            }

            //if (IsVideoDocument(message.Document) && TryAddUniqueId(GetUniqueId(message.Document!), uniqueIds))
            //{
            //    var link = await MediaToLink(message.Document!, cancellationToken).ConfigureAwait(false);
            //    var uri = new Uri(link);
            //    content.Add(CreateLazyVideo(uri, message.Document!.MimeType));
            //}
        }

        private async Task<List<ContentItem>> AddDocuments(Message castedMessage, HashSet<string> uniqueIds, CancellationToken cancellationToken)
        {
            List<ContentItem> content = [];

            if (IsPdfDocument(castedMessage.ReplyToMessage?.Document)
                && TryAddUniqueId(GetUniqueId(castedMessage.ReplyToMessage!.Document!), uniqueIds))
            {
                var link = await MediaToLink(castedMessage.ReplyToMessage!.Document!, cancellationToken).ConfigureAwait(false);
                var uri = new Uri(link);
                content.Add(CreateLazyDocument(uri, castedMessage.ReplyToMessage.Document!.MimeType));
            }

            if (IsPdfDocument(castedMessage.Document)
                && TryAddUniqueId(GetUniqueId(castedMessage.Document!), uniqueIds))
            {
                var link = await MediaToLink(castedMessage.Document!, cancellationToken).ConfigureAwait(false);
                var uri = new Uri(link);
                content.Add(CreateLazyDocument(uri, castedMessage.Document!.MimeType));
            }

            return content;
        }

        private async Task<List<ContentItem>> GetSticker(Message castedMessage, HashSet<string> uniqueIds, CancellationToken cancellationToken)
        {
            List<ContentItem> content = [];
            if (castedMessage.Sticker?.Thumbnail != null
                && TryAddUniqueId(GetUniqueId(castedMessage.Sticker.Thumbnail), uniqueIds))
            {
                var link = await PhotoToLink([castedMessage.Sticker.Thumbnail], cancellationToken).ConfigureAwait(false);
                var uri = new Uri(link);
                content.Add(CreateLazyImage(uri));
            }

            return content;
        }

        private async Task<List<ContentItem>> GetPhotos(Message castedMessage, HashSet<string> uniqueIds, CancellationToken cancellationToken)
        {
            List<ContentItem> content = [];

            if (castedMessage.ReplyToMessage?.Photo != null
                && TryAddUniqueId(GetUniqueId(castedMessage.ReplyToMessage.Photo.Last()), uniqueIds))
            {
                string replyToPhotoLink = castedMessage.ReplyToMessage?.Photo != null
                        ? await PhotoToLink(castedMessage.ReplyToMessage.Photo, cancellationToken).ConfigureAwait(false)
                        : string.Empty;

                if (!string.IsNullOrEmpty(replyToPhotoLink))
                {
                    var uri = new Uri(replyToPhotoLink);
                    content.Add(CreateLazyImage(uri));
                }
            }

            if (castedMessage.Photo != null
                && TryAddUniqueId(GetUniqueId(castedMessage.Photo.Last()), uniqueIds))
            {
                string photoLink = await PhotoToLink(castedMessage.Photo, cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(photoLink))
                {
                    var uri = new Uri(photoLink);
                    content.Add(CreateLazyImage(uri));
                }
            }

            return content;
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

            if (string.IsNullOrWhiteSpace(document.FileName))
            {
                return false;
            }

            if (document.FileName.StartsWith("animation.", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(document.MimeType))
            {
                return document.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
            }

            return document.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                   || document.FileName.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)
                   || document.FileName.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)
                   || document.FileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
                   || document.FileName.EndsWith(".avi", StringComparison.OrdinalIgnoreCase)
                   || document.FileName.EndsWith(".mpeg", StringComparison.OrdinalIgnoreCase)
                   || document.FileName.EndsWith(".mpg", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetForwardedFrom(MessageOrigin? origin)
        {
            string forwardedFrom = "";
            if (origin != null)
            {
                switch (origin)
                {
                    case MessageOriginChannel messageOriginChannel:
                        forwardedFrom = $"channel \"{messageOriginChannel.Chat.Title}\"(@{messageOriginChannel.Chat.Username})";
                        break;

                    case MessageOriginChat messageOriginChat:
                        forwardedFrom = $"chat \"{messageOriginChat.SenderChat.Title}\"(@{messageOriginChat.SenderChat.Username})";
                        break;

                    case MessageOriginHiddenUser messageOriginHiddenUser:
                        forwardedFrom = $"hidden user \"{messageOriginHiddenUser.SenderUserName}\"";
                        break;

                    case MessageOriginUser messageOriginUser:
                        forwardedFrom = $"user \"{CompoundUserName(messageOriginUser.SenderUser)}\"";
                        break;
                }
            }

            return forwardedFrom;
        }

        private static bool TryAddUniqueId(string? uniqueId, HashSet<string> uniqueIds)
        {
            if (string.IsNullOrWhiteSpace(uniqueId))
            {
                return true;
            }

            return uniqueIds.Add(uniqueId);
        }

        private static string? GetUniqueId(FileBase file)
        {
            if (!string.IsNullOrWhiteSpace(file.FileUniqueId))
            {
                return file.FileUniqueId;
            }

            return file.FileId;
        }

        private static string? GetUniqueId(PhotoSize photo)
        {
            if (!string.IsNullOrWhiteSpace(photo.FileUniqueId))
            {
                return photo.FileUniqueId;
            }

            return photo.FileId;
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

        // Generic template method for any Telegram media deriving from FileBase.
        private async Task<string> MediaToLink<TFile>(TFile media,
            CancellationToken cancellationToken = default) where TFile : FileBase
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return await Task.FromCanceled<string>(cancellationToken).ConfigureAwait(false);
            }

            var file = await Bot.GetFile(media.FileId, cancellationToken).ConfigureAwait(false);
            return BuildFileUri(file.FilePath);
        }

        private string BuildFileUri(string? filePath)
        {
            return $"{new Uri(new Uri($"{TelegramBotFile}{telegramBotKey}/"), filePath)}";
        }

        private ImageContentItem CreateLazyImage(Uri uri)
        {
            return new ImageContentItem
            {
                ImageUrl = uri,
                Loader = contentHelper.EncodeImageToWebpBase64
            };
        }

        private AudioContentItem CreateLazyAudio(Uri uri)
        {
            return new AudioContentItem
            {
                AudioUrl = uri,
                Loader = contentHelper.EncodeAudioToBase64
            };
        }

        private DocumentContentItem CreateLazyDocument(Uri uri, string? mimeType)
        {
            var item = new DocumentContentItem
            {
                DocumentUrl = uri,
                Loader = contentHelper.EncodeFileToBase64
            };

            if (!string.IsNullOrWhiteSpace(mimeType))
            {
                item.MimeType = mimeType;
            }

            return item;
        }

        private VideoContentItem CreateLazyVideo(Uri uri, string? mimeType)
        {
            var item = new VideoContentItem
            {
                VideoUrl = uri,
                Loader = contentHelper.EncodeFileToBase64
            };

            if (!string.IsNullOrWhiteSpace(mimeType))
            {
                item.MimeType = mimeType;
            }

            return item;
        }

        [GeneratedRegex("[^a-zA-Z0-9_\\s-]")]
        private static partial Regex WrongNameSymbolsRegexpCreator();
    }
}
