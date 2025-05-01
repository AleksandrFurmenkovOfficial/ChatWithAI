using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IChatCommandProcessor
    {
        Task<bool> ExecuteIfChatCommand(
            IChat chat,
            ChatMessage message,
            CancellationToken cancellationToken = default);
    }
}