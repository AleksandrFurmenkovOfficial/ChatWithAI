using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IChatMessageAction
    {
        ActionId GetId { get; }
        Task Run(IChat chat, CancellationToken cancellationToken = default);
    }
}