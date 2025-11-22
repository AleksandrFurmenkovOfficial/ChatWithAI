using ChatWithAI.Contracts.Model;

namespace ChatWithAI.Core.StateMachine
{
    /// <summary>
    /// Base class for all trigger contexts.
    /// </summary>
    public abstract record TriggerContext;

    /// <summary>
    /// Empty context for triggers that don't require parameters.
    /// </summary>
    public sealed record VoidContext : TriggerContext
    {
        public static readonly VoidContext Instance = new();
        private VoidContext() { }
    }

    /// <summary>
    /// Context for triggers that require a CancellationToken.
    /// </summary>
    public sealed record CancellableContext(CancellationToken CancellationToken) : TriggerContext;

    /// <summary>
    /// Context for adding messages to the chat.
    /// </summary>
    public sealed record AddMessagesContext(List<ChatMessageModel> Messages) : TriggerContext;

    /// <summary>
    /// Context for setting chat mode.
    /// </summary>
    public sealed record SetModeContext(ChatMode Mode) : TriggerContext;

    /// <summary>
    /// Context passed to the Streaming state containing the response stream from AI agent.
    /// </summary>
    public sealed record StreamingContext(IAiStreamingResponse ResponseStream, CancellationToken CancellationToken) : TriggerContext;
}
