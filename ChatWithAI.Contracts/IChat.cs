using ChatWithAI.Contracts.Model;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IChat
    {
        string Id { get; }

        ChatMode GetMode();
        Task SetMode(ChatMode mode);
        Task Reset();

        Task AddMessages(List<ChatMessageModel> messages);
        Task DoResponseToLastMessage(CancellationToken ct);
        Task ContinueLastResponse(CancellationToken ct);
        Task RegenerateLastResponse(CancellationToken ct);
    }
}