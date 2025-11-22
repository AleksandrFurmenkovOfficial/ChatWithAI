using System;
using System.Collections.Generic;
using System.Threading;

namespace ChatWithAI.Contracts
{
    /// <summary>
    /// Represents a streamed AI response as an async sequence of text deltas,
    /// plus optional structured content available after completion.
    /// </summary>
    public interface IAiStreamingResponse : IStructuredResponse, IAsyncDisposable
    {
        /// <summary>
        /// Returns user-visible text deltas as they arrive from the provider.
        /// </summary>
        IAsyncEnumerable<string> GetTextDeltasAsync(CancellationToken cancellationToken = default);
    }
}
