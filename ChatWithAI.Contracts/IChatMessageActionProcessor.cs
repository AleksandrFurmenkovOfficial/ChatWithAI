using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IChatMessageActionProcessor
    {
        Task HandleMessageAction(
            IChat chat,
            ActionParameters actionCallParameters,
            CancellationToken cancellationToken = default);
    }
}