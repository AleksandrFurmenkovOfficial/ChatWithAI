using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    /// <summary>
    /// Data transfer object for sending messages to messenger.
    /// Contains only the data needed by the messenger to send/display a message.
    /// </summary>
    public sealed class MessengerMessageDTO
    {
        public string TextContent { get; set; } = string.Empty;
        public List<ContentItem> MediaContent { get; set; } = [];
        public MessageRole Role { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public interface IMessenger
    {
        int MaxTextMessageLen();

        int MaxPhotoMessageLen();

        Task<bool> DeleteMessage(string chatId, MessageId messageId);

        Task<string> SendTextMessage(string chatId, MessengerMessageDTO message,
            IEnumerable<ActionId>? messageActionIds = null);

        Task EditTextMessage(string chatId, MessageId messageId, string content,
            IEnumerable<ActionId>? messageActionIds = null);

        Task<string> SendPhotoMessage(string chatId, MessengerMessageDTO message,
            IEnumerable<ActionId>? messageActionIds = null);

        Task EditPhotoMessage(string chatId, MessageId messageId, string caption,
            IEnumerable<ActionId>? messageActionIds = null);
    }
}