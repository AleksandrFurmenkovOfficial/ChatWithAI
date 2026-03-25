using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IChatProcessor
    {
        Task RunEventLoop(CancellationToken cancellationToken = default);
    }
}