using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IMemoryStorage
    {
        Task<string> GetContent(string mode, string chatId, CancellationToken cancellationToken = default);
        Task AddLineContent(string mode, string chatId, string line, CancellationToken cancellationToken = default);
        void Remove(string mode, string chatId, CancellationToken cancellationToken = default);
    }
}
