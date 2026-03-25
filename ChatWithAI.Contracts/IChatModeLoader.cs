using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IChatModeLoader
    {
        Task<ChatMode> GetChatMode(
            string modeName,
            CancellationToken cancellationToken = default);
    }
}