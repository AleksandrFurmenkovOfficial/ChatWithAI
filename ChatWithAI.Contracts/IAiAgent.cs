using ChatWithAI.Contracts.Model;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IAiAgent : IAiSimpleResponseGetter, IAiImagePainter
    {
        string AiName { get; }

        /// <summary>
        /// Gets a streamed response from the AI agent.
        /// Returns an async sequence of text deltas and optional structured content.
        /// </summary>
        /// <param name="userId">The user ID for the conversation.</param>
        /// <param name="messages">The conversation messages to send to the AI.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the request.</param>
        /// <returns>A streamed response containing the AI output.</returns>
        Task<IAiStreamingResponse> GetResponseStreamAsync(
            string userId,
            IEnumerable<ChatMessageModel> messages,
            CancellationToken cancellationToken = default);
    }
}
