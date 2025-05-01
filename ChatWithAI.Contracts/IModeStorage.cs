using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IModeStorage
    {
        Task<string> GetContent(
            string modeName,
            CancellationToken cancellationToken = default);
    }
}
