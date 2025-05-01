using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IAiAgent : IAiSimpleResponseGetter, IAiImagePainter
    {
        string AiName { get; }

        Task GetResponse(string chatId,
            IEnumerable<ChatMessage> messages,
            Func<ResponseStreamChunk, Task<bool>> responseStreamChunkGetter,
            CancellationToken cancellationToken = default);
    }
}