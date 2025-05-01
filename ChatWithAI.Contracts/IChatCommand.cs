using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IChatCommand
    {
        string Name { get; }
        bool IsAdminOnlyCommand { get; }
        Task Execute(IChat chat, ChatMessage message, CancellationToken cancellationToken = default);
    }
}