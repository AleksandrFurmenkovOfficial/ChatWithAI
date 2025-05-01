using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IChatMessageProcessor
    {
        Task HandleMessage(
            IChat chat,
            ChatMessage message,
            CancellationToken cancellationToken = default);
    }
}