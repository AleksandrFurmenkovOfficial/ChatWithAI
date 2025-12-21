using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IMemoryStorage
    {
        Task<string> GetContent(string chatId, string mode, CancellationToken cancellationToken = default);
        Task AddLineContent(string chatId, string mode, string line, CancellationToken cancellationToken = default);
        void Remove(string chatId, string mode, CancellationToken cancellationToken = default);
    }
}
