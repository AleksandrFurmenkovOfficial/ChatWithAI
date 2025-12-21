using ChatWithAI.Contracts.Model;
using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IChatMessageConverter
    {
        Task<ChatMessageModel> ConvertToChatMessage(
            object rawMessage,
            CancellationToken cancellationToken = default);
    }
}