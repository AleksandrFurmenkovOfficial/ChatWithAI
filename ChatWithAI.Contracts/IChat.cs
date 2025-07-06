using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IChat
    {
        string Id { get; }

        void AddMessages(List<ChatMessage> messages);
        Task DoResponseToLastMessage(CancellationToken cancellationToken = default);

        void SetMode(ChatMode mode);
        ChatMode GetMode();

        void RecreateAiAgent();

        Task SendSomethingGoesWrong(CancellationToken cancellationToken = default);
        Task SendSystemMessage(string content, CancellationToken cancellationToken = default);
        Task RemoveResponse(CancellationToken cancellationToken = default);
        Task Reset(CancellationToken cancellationToken = default);
        Task RegenerateLastResponse(CancellationToken cancellationToken = default);
        Task ContinueLastResponse(CancellationToken cancellationToken = default);
    }
}