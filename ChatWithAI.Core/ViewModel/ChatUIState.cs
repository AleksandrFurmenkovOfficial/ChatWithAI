using ChatWithAI.Contracts.Model;

namespace ChatWithAI.Core.ViewModel
{
    /// <summary>
    /// Manages the UI state for a chat, including message splitting for length limits.
    /// Acts as the ViewModel layer between the Model (ChatHistoryModel) and the View (IMessenger).
    /// </summary>
    public sealed class ChatUIState
    {
        private readonly string _chatId;
        private readonly int _maxTextMessageLen;
        private readonly int _maxPhotoMessageLen;
        private readonly Dictionary<ModelMessageId, List<UIMessageViewModel>> _messagesByModelId;
        private UIMessageViewModel? _messageWithActiveButtons;

        public ChatUIState(string chatId, int maxTextMessageLen, int maxPhotoMessageLen)
        {
            _chatId = chatId;
            _maxTextMessageLen = maxTextMessageLen;
            _maxPhotoMessageLen = maxPhotoMessageLen;
            _messagesByModelId = [];
        }

        public string ChatId => _chatId;
        public int MaxTextMessageLen => _maxTextMessageLen;

        /// <summary>
        /// Gets all UI messages for a model message.
        /// </summary>
        public List<UIMessageViewModel> GetUIMessages(ModelMessageId modelId)
        {
            return _messagesByModelId.TryGetValue(modelId, out var messages) ? messages : [];
        }

        /// <summary>
        /// Gets the last UI message for a model message (for buttons, updates).
        /// </summary>
        public UIMessageViewModel? GetLastUIMessage(ModelMessageId modelId)
        {
            var messages = GetUIMessages(modelId);
            return messages.Count > 0 ? messages[^1] : null;
        }

        /// <summary>
        /// Gets the message that currently has active buttons.
        /// </summary>
        public UIMessageViewModel? GetMessageWithActiveButtons() => _messageWithActiveButtons;

        /// <summary>
        /// Creates initial UI message for a model message (before content is known).
        /// Used when starting to stream a response.
        /// </summary>
        public UIMessageViewModel CreateInitialUIMessage(ChatMessageModel modelMessage, List<ActionId>? buttons = null)
        {
            var uiMessage = new UIMessageViewModel
            {
                ParentModelId = modelMessage.Id,
                SegmentIndex = 0,
                TextContent = string.Empty,
                Role = modelMessage.Role,
                Name = modelMessage.Name,
                IsSent = false
            };

            if (buttons != null && buttons.Count > 0)
            {
                SetActiveButtons(uiMessage, buttons);
            }

            _messagesByModelId[modelMessage.Id] = [uiMessage];
            return uiMessage;
        }

        /// <summary>
        /// Registers a sent UI message (after messenger confirms send).
        /// </summary>
        public static void MarkAsSent(UIMessageViewModel message, MessageId messengerMessageId)
        {
            message.MessengerMessageId = messengerMessageId;
            message.IsSent = true;
        }

        /// <summary>
        /// Creates additional UI message when content exceeds limit (for splitting).
        /// </summary>
        public UIMessageViewModel CreateNextSegment(ModelMessageId parentModelId, MessageRole role, string name, List<ActionId>? buttons = null)
        {
            var messages = GetUIMessages(parentModelId);
            var nextIndex = messages.Count;

            var uiMessage = new UIMessageViewModel
            {
                ParentModelId = parentModelId,
                SegmentIndex = nextIndex,
                TextContent = string.Empty,
                Role = role,
                Name = name,
                IsSent = false
            };

            if (buttons != null && buttons.Count > 0)
            {
                SetActiveButtons(uiMessage, buttons);
            }

            messages.Add(uiMessage);
            return uiMessage;
        }

        /// <summary>
        /// Sets active buttons on a message.
        /// </summary>
        public void SetActiveButtons(UIMessageViewModel message, List<ActionId>? buttons)
        {
            // Clear previous active buttons
            if (_messageWithActiveButtons != null && _messageWithActiveButtons != message)
            {
                _messageWithActiveButtons.ActiveButtons = null;
            }

            message.ActiveButtons = buttons;
            _messageWithActiveButtons = message.IsButtonsActive ? message : null;
        }

        /// <summary>
        /// Clears active buttons from all messages.
        /// Returns the message that had buttons (if any).
        /// </summary>
        public UIMessageViewModel? ClearActiveButtons()
        {
            var prev = _messageWithActiveButtons;
            if (prev != null)
            {
                prev.ActiveButtons = null;
                _messageWithActiveButtons = null;
            }
            return prev;
        }

        /// <summary>
        /// Removes all UI messages for a model message.
        /// Returns the removed messages (for deletion from messenger).
        /// </summary>
        public List<UIMessageViewModel> RemoveUIMessages(ModelMessageId modelId)
        {
            if (_messagesByModelId.TryGetValue(modelId, out var messages))
            {
                _messagesByModelId.Remove(modelId);

                // Clear active buttons if they were on one of these messages
                if (_messageWithActiveButtons != null && messages.Contains(_messageWithActiveButtons))
                {
                    _messageWithActiveButtons = null;
                }

                return messages;
            }
            return [];
        }

        /// <summary>
        /// Removes the last UI message for a model message.
        /// Returns the removed message (for deletion from messenger).
        /// </summary>
        public UIMessageViewModel? RemoveLastUIMessage(ModelMessageId modelId)
        {
            if (_messagesByModelId.TryGetValue(modelId, out var messages) && messages.Count > 0)
            {
                var lastMsg = messages[^1];
                messages.RemoveAt(messages.Count - 1);

                // Clear active buttons if they were on this message
                if (_messageWithActiveButtons == lastMsg)
                {
                    _messageWithActiveButtons = null;
                }

                return lastMsg;
            }
            return null;
        }

        /// <summary>
        /// Splits text content into segments based on max length.
        /// </summary>
        public static List<string> SplitTextByLength(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return [text ?? string.Empty];
            }

            var segments = new List<string>();
            var remaining = text;

            while (remaining.Length > maxLength)
            {
                segments.Add(remaining[..maxLength]);
                remaining = remaining[maxLength..];
            }

            if (remaining.Length > 0)
            {
                segments.Add(remaining);
            }

            return segments;
        }

        /// <summary>
        /// Checks if text would require splitting.
        /// </summary>
        public bool WouldRequireSplitting(string text)
        {
            return !string.IsNullOrEmpty(text) && text.Length > _maxTextMessageLen;
        }

        /// <summary>
        /// Clears all UI state.
        /// </summary>
        public void Clear()
        {
            _messagesByModelId.Clear();
            _messageWithActiveButtons = null;
        }
    }
}
