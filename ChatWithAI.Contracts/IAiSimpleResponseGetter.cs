using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IAiSimpleResponseGetter
    {
        Task<string> GetResponse(
            string userId,
            string setting,
            string question,
            string? data,
            CancellationToken cancellationToken = default);
    }
}