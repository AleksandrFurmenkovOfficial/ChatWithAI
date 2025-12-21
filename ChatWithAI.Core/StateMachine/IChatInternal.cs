using ChatWithAI.Contracts.Model;

namespace ChatWithAI.Core.StateMachine
{
    /// <summary>
    /// Internal interface for Chat operations called by ChatStateMachine.
    /// Contains all business logic operations triggered by state machine transitions.
    /// </summary>
    public interface IChatInternal
    {
        /// <summary>
        /// Gets the chat identifier.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Gets the current chat mode.
        /// </summary>
        ChatMode GetMode();

        // === State Entry/Exit Operations ===

        /// <summary>
        /// Called when entering WaitingForFirstMessage state.
        /// Resets the chat and sends the start warning message.
        /// </summary>
        Task OnEnterWaitingForFirstMessageAsync();

        /// <summary>
        /// Called when entering Error state.
        /// Sends an error message to the user.
        /// </summary>
        Task OnEnterErrorAsync();

        /// <summary>
        /// Called when exiting Error state.
        /// Removes the error message from the chat.
        /// </summary>
        Task OnExitErrorAsync();

        // === Trigger Operations ===

        /// <summary>
        /// Adds messages to the chat.
        /// </summary>
        Task AddUserMessagesToChatHistoryAsync(List<ChatMessageModel> messages, bool forceOldTurn = false);

        /// <summary>
        /// Sets the chat mode and recreates the AI agent.
        /// </summary>
        Task SetModeAsync(ChatMode mode);

        /// <summary>
        /// Initiates AI response to the last user message.
        /// Returns the streaming context if successful, or the trigger to fire next.
        /// </summary>
        Task<ChatOperationResult> InitiateResponseAsync(CancellationToken ct);

        /// <summary>
        /// Continues the last AI response by adding a "please continue" system message.
        /// Returns the streaming context if successful, or the trigger to fire next.
        /// </summary>
        Task<ChatOperationResult> ContinueResponseAsync(CancellationToken ct);

        /// <summary>
        /// Regenerates the last AI response by removing previous response and requesting new one.
        /// Returns the streaming context if successful, or the trigger to fire next.
        /// </summary>
        Task<ChatOperationResult> RegenerateResponseAsync(CancellationToken ct);

        /// <summary>
        /// Processes the response stream from the AI agent.
        /// Returns the trigger to fire when streaming completes.
        /// </summary>
        Task<ChatTrigger> ProcessResponseStreamAsync(IAiStreamingResponse responseStream, CancellationToken ct);
    }

    /// <summary>
    /// Result of a chat operation that may trigger a state transition.
    /// </summary>
    public sealed record ChatOperationResult
    {
        /// <summary>
        /// The streaming context if the operation was successful and streaming should begin.
        /// </summary>
        public StreamingContext? StreamingContext { get; init; }

        /// <summary>
        /// The trigger to fire if the operation failed or was cancelled.
        /// </summary>
        public ChatTrigger? NextTrigger { get; init; }

        /// <summary>
        /// Indicates whether the operation was successful.
        /// </summary>
        public bool IsSuccess => StreamingContext != null;

        public static ChatOperationResult Success(StreamingContext context) => new() { StreamingContext = context };
        public static ChatOperationResult Failure(ChatTrigger trigger) => new() { NextTrigger = trigger };
    }
}
