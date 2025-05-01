using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IChatMessageConverter
    {
        public Task<ChatMessage> ConvertToChatMessage(
            object rawMessage,
            CancellationToken cancellationToken = default);
    }
}