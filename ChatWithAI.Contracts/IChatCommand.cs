using ChatWithAI.Contracts.Model;
using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IChatCommand
    {
        string Name { get; }
        bool IsAdminOnlyCommand { get; }
        Task Execute(IChat chat, ChatMessageModel message, CancellationToken cancellationToken = default);
    }
}