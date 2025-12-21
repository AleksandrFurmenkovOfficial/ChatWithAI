using ChatWithAI.Contracts.Model;
using ChatWithAI.Core.ChatMessageActions;
using ChatWithAI.Core.StateMachine;
using ChatWithAI.Core.ViewModel;
using System.Text;

namespace ChatWithAI.Core
{
    public sealed partial class Chat : IChatInternal, IDisposable
    {
        private const int MessageUpdateStepInCharsCount = 168;

        // === IChatInternal Implementation ===

        public async Task<ChatTrigger> ProcessResponseStreamAsync(IAiStreamingResponse responseStream, CancellationToken ct)
        {
            logger.LogDebugMessage($"Chat {Id}: ProcessResponseStreamAsync");

            if (aiAgent == null)
                throw new InvalidOperationException("AI agent is not initialized");

            await using var _ = responseStream;

            try
            {
                var chatState = GetOrCreateState();
                var modelMsg = chatState.History.GetLastAssistantMessage();
                if (modelMsg == null)
                {
                    logger.LogErrorMessage($"Chat {Id}: No response message to update");
                    return ChatTrigger.AIResponseError;
                }

                var streamingState = new StreamingState(modelMsg, chatState.UIState, messenger.MaxTextMessageLen());

                await ReadTextDeltasIntoBuilderAsync(responseStream, streamingState, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                await FinalizeResponseContentAsync(responseStream, streamingState).ConfigureAwait(false);
                await SplitContentIfExceedsLimitAsync(streamingState).ConfigureAwait(false);

                // Update final message with Continue/Regenerate buttons
                var lastUIMsg = streamingState.CurrentUIMessage;
                if (lastUIMsg != null)
                {
                    chatState.UIState.SetActiveButtons(lastUIMsg, [ContinueAction.Id, RegenerateAction.Id]);
                    await UpdateUIMessageInMessenger(lastUIMsg, [ContinueAction.Id, RegenerateAction.Id]).ConfigureAwait(false);
                }

                SaveState(chatState);

                logger.LogDebugMessage($"Chat {Id}: Stream processing completed successfully");
                return ChatTrigger.AIResponseFinished;
            }
            catch (OperationCanceledException)
            {
                await OnCancelProcessResponseStreamAsync().ConfigureAwait(false);
                return ChatTrigger.UserStop;
            }
            catch (Exception ex)
            {
                await OnErrorProcessResponseStreamAsync(ex).ConfigureAwait(false);
                return ChatTrigger.AIResponseError;
            }
        }

        private async Task OnCancelProcessResponseStreamAsync()
        {
            logger.LogDebugMessage($"Chat {Id}: Response request was stopped");
            await CleanupAfterExceptionInProcessResponseStreamAsync([ContinueAction.Id, RegenerateAction.Id]).ConfigureAwait(false);
        }

        private async Task OnErrorProcessResponseStreamAsync(Exception ex)
        {
            logger.LogErrorMessage($"Chat {Id}: Error processing response stream: {ex.Message}");
            await CleanupAfterExceptionInProcessResponseStreamAsync(null).ConfigureAwait(false);
        }

        private async Task CleanupAfterExceptionInProcessResponseStreamAsync(IEnumerable<ActionId>? newActions)
        {
            var chatState = GetOrCreateState();
            var modelMsg = chatState.History.GetLastAssistantMessage();
            if (modelMsg != null)
            {
                // 1. Remove empty UI segments from the end
                // We keep removing segments as long as they are empty (text and media)
                // BUT we ensure we don't remove a segment if it has some content, OR if it's the only one left?
                // User said: "if segment is empty - it is deleted".
                while (true)
                {
                    var lastUI = chatState.UIState.GetLastUIMessage(modelMsg.Id);
                    if (lastUI == null) break;

                    bool isEmpty = string.IsNullOrEmpty(lastUI.TextContent) && lastUI.MediaContent.Count == 0;
                    if (isEmpty)
                    {
                        var removed = chatState.UIState.RemoveLastUIMessage(modelMsg.Id);
                        if (removed != null && removed.IsSent)
                        {
                            await messenger.DeleteMessage(Id, removed.MessengerMessageId).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                // 2. Update buttons on the new last UI segment
                var newLastUI = chatState.UIState.GetLastUIMessage(modelMsg.Id);
                if (newLastUI != null)
                {
                    chatState.UIState.SetActiveButtons(newLastUI, newActions?.ToList());
                    await UpdateUIMessageInMessenger(newLastUI, newActions).ConfigureAwait(false);
                }
            }
            SaveState(chatState);
        }

        private sealed class StreamingState
        {
            public ChatMessageModel ModelMessage { get; }
            public ChatUIState UIState { get; }
            public UIMessageViewModel? CurrentUIMessage { get; set; }
            public StringBuilder ContentBuilder { get; } = new();
            public StringBuilder FullContentBuilder { get; } = new(); // Tracks all content for model
            public int MaxMessageLen { get; }
            public int CharsSinceLastUpdate { get; set; }
            public bool HasOverflowed { get; set; }

            public StreamingState(ChatMessageModel modelMsg, ChatUIState uiState, int maxLen)
            {
                ModelMessage = modelMsg;
                UIState = uiState;
                MaxMessageLen = maxLen;

                // Get the initial UI message that was created when response started
                var uiMessages = uiState.GetUIMessages(modelMsg.Id);
                CurrentUIMessage = uiMessages.Count > 0 ? uiMessages[0] : null;
            }

            public void Add(string chunk)
            {
                ContentBuilder.Append(chunk);
                FullContentBuilder.Append(chunk);
                CharsSinceLastUpdate += chunk.Length;
            }
        }

        private async Task ReadTextDeltasIntoBuilderAsync(IAiStreamingResponse responseStream, StreamingState state, CancellationToken ct)
        {
            await foreach (var chunk in responseStream.GetTextDeltasAsync(ct).ConfigureAwait(false))
            {
                if (string.IsNullOrEmpty(chunk))
                {
                    continue;
                }

                state.Add(chunk);

                if (state.ContentBuilder.Length >= state.MaxMessageLen)
                {
                    await HandleMessageOverflowAsync(state).ConfigureAwait(false);
                }
                else if (state.CharsSinceLastUpdate >= MessageUpdateStepInCharsCount)
                {
                    await UpdateStreamingProgressAsync(state).ConfigureAwait(false);
                }
            }
        }

        private async Task HandleMessageOverflowAsync(StreamingState state)
        {
            // On first overflow, we need to handle the transition to multiple UI messages
            if (!state.HasOverflowed)
            {
                await SwitchToMultipleUIMessagesAsync(state).ConfigureAwait(false);
            }

            // Finalize current UI message with content up to max length
            var currentContent = state.ContentBuilder.ToString(0, state.MaxMessageLen);
            if (state.CurrentUIMessage != null)
            {
                state.CurrentUIMessage.TextContent = currentContent;
                await UpdateUIMessageInMessenger(state.CurrentUIMessage, null).ConfigureAwait(false);
            }

            // Keep remaining content for next message
            var remainingContent = state.ContentBuilder.ToString(state.MaxMessageLen, state.ContentBuilder.Length - state.MaxMessageLen);
            state.ContentBuilder.Clear();
            state.ContentBuilder.Append(remainingContent);

            // Create next UI message for continuation
            state.CurrentUIMessage = await CreateNextUISegmentAsync(state).ConfigureAwait(false);
            state.CharsSinceLastUpdate = remainingContent.Length;
        }

        private static Task SwitchToMultipleUIMessagesAsync(StreamingState state)
        {
            state.HasOverflowed = true;
            return Task.CompletedTask;
        }

        private async Task<UIMessageViewModel> CreateNextUISegmentAsync(StreamingState state)
        {
            var uiMessage = state.UIState.CreateNextSegment(
                state.ModelMessage.Id,
                state.ModelMessage.Role,
                state.ModelMessage.Name,
                [StopAction.Id]);

            uiMessage.TextContent = Strings.InitAnswerTemplate;

            // Send the new segment to messenger
            await SendUIMessageToMessenger(uiMessage).ConfigureAwait(false);

            uiMessage.TextContent = string.Empty;
            return uiMessage;
        }

        private async Task UpdateStreamingProgressAsync(StreamingState state)
        {
            if (state.CurrentUIMessage == null) return;

            state.CurrentUIMessage.TextContent = state.ContentBuilder.ToString();
            await UpdateUIMessageInMessenger(state.CurrentUIMessage, [StopAction.Id]).ConfigureAwait(false);
            state.CharsSinceLastUpdate = 0;
        }

        private async Task FinalizeResponseContentAsync(IAiStreamingResponse responseStream, StreamingState state)
        {
            // Get structured content if available
            var structuredContent = responseStream.GetStructuredContent();

            // Determine the full content for model (history)
            var fullText = state.FullContentBuilder.ToString();

            // Store full content in model message for chat history
            state.ModelMessage.Content.Clear();
            if (structuredContent != null && structuredContent.Count > 0)
            {
                // Use structured content (may contain images, etc.)
                foreach (var item in structuredContent)
                {
                    state.ModelMessage.Content.Add(item);
                }
            }
            else if (!string.IsNullOrEmpty(fullText))
            {
                state.ModelMessage.AddTextContent(fullText);
            }
            else
            {
                throw new InvalidOperationException("AI response is empty");
            }

            // Update current UI message with remaining content (Text only)
            if (state.CurrentUIMessage != null)
            {
                // Finalize text content for current segment
                var textContent = state.HasOverflowed ? state.ContentBuilder.ToString() : fullText;
                state.CurrentUIMessage.TextContent = textContent;

                // Handle case where text is empty (Image only response)
                if (string.IsNullOrEmpty(textContent))
                {
                    // If this segment is empty and we have media to come, or if it was just a placeholder
                    // We should remove it to avoid empty bubbles.
                    // BUT: if this was the "Thinking..." bubble, we want it gone before showing images.
                    var removed = state.UIState.RemoveLastUIMessage(state.ModelMessage.Id);
                    if (removed != null && removed.IsSent)
                    {
                        await messenger.DeleteMessage(Id, removed.MessengerMessageId).ConfigureAwait(false);
                    }
                    state.CurrentUIMessage = null;
                }
                else
                {
                    // Update the text message in messenger
                    // We do NOT add media here anymore.
                    await UpdateUIMessageInMessenger(state.CurrentUIMessage, null).ConfigureAwait(false);

                    // Ensure the text segment is split if it exceeds limits BEFORE we switch to image segments
                    await SplitContentIfExceedsLimitAsync(state).ConfigureAwait(false);
                }
            }

            // Process non-text items (Images) as NEW segments
            if (structuredContent != null)
            {
                var nonTextItems = structuredContent.Where(i => i is not TextContentItem).ToList();
                foreach (var item in nonTextItems)
                {
                    if (item is not ImageContentItem) continue; // Now only images are supported in special segments;

                    // Create a new segment for each media item (or group if we supported albums, but currently one by one is safer for mixed types)
                    var nextSegment = state.UIState.CreateNextSegment(
                        state.ModelMessage.Id,
                        state.ModelMessage.Role,
                        state.ModelMessage.Name);

                    nextSegment.TextContent = string.Empty; // Caption could go here if attached to image, but usually we split text / image
                    nextSegment.MediaContent.Add(item);

                    // Send immediately
                    await SendUIMessageToMessenger(nextSegment).ConfigureAwait(false);

                    // Update state tracking
                    state.CurrentUIMessage = nextSegment;
                }
            }
        }

        private async Task SplitContentIfExceedsLimitAsync(StreamingState state)
        {
            if (state.CurrentUIMessage == null) return;

            // Check if the current segment still exceeds limit
            var currentText = state.CurrentUIMessage.TextContent;
            if (string.IsNullOrEmpty(currentText) || currentText.Length <= state.MaxMessageLen)
                return;

            // Need to split the current segment further
            var remaining = currentText;
            while (remaining.Length > state.MaxMessageLen)
            {
                state.CurrentUIMessage.TextContent = remaining[..state.MaxMessageLen];
                await UpdateUIMessageInMessenger(state.CurrentUIMessage, null).ConfigureAwait(false);
                remaining = remaining[state.MaxMessageLen..];

                state.CurrentUIMessage = await CreateNextUISegmentAsync(state).ConfigureAwait(false);
            }

            // Set final remaining content
            state.CurrentUIMessage.TextContent = remaining;

            // Re-add non-text items to last segment
            // (already added in FinalizeResponseContentAsync, but we moved to a new segment)
        }
    }
}
