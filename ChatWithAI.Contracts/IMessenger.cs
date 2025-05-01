using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IMessenger
    {
        int MaxTextMessageLen();

        int MaxPhotoMessageLen();

        Task<bool> DeleteMessage(string chatId, MessageId messageId, CancellationToken cancellationToken = default);

        Task<string> SendTextMessage(string chatId, ChatMessage message,
            IEnumerable<ActionId>? messageActionIds = null, CancellationToken cancellationToken = default);

        Task EditTextMessage(string chatId, MessageId messageId, string content,
            IEnumerable<ActionId>? messageActionIds = null, CancellationToken cancellationToken = default);

        Task<string> SendPhotoMessage(string chatId, ChatMessage message,
            IEnumerable<ActionId>? messageActionIds = null, CancellationToken cancellationToken = default);

        Task EditPhotoMessage(string chatId, MessageId messageId, string caption,
            IEnumerable<ActionId>? messageActionIds = null, CancellationToken cancellationToken = default);
    }
}