using ChatWithAI.Contracts.Model;

namespace ChatWithAI.Core.ViewModel
{
    /// <summary>
    /// Represents a single UI message that can be displayed in the messenger.
    /// One model message (ChatMessageModel) may be split into multiple UIMessageViewModels
    /// if the content exceeds the messenger's length limit.
    /// </summary>
    public sealed class UIMessageViewModel
    {
        /// <summary>
        /// The messenger-specific message ID (e.g., Telegram message ID).
        /// </summary>
        public MessageId MessengerMessageId { get; set; }

        /// <summary>
        /// Reference to the parent model message.
        /// </summary>
        public ModelMessageId ParentModelId { get; set; }

        /// <summary>
        /// Segment index when the model message is split (0, 1, 2, ...).
        /// </summary>
        public int SegmentIndex { get; set; }

        /// <summary>
        /// The text content for this specific UI message segment.
        /// </summary>
        public string TextContent { get; set; }

        /// <summary>
        /// Non-text content items (images, audio) - only on the last segment.
        /// </summary>
        public List<ContentItem> MediaContent { get; set; }

        /// <summary>
        /// Whether this message has been sent to the messenger.
        /// </summary>
        public bool IsSent { get; set; }

        /// <summary>
        /// Whether this message was deleted by the user in messenger.
        /// Deleted messages should not be edited or have content downloaded.
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// Current buttons on this message.
        /// </summary>
        public List<ActionId>? ActiveButtons { get; set; }

        /// <summary>
        /// Role of the message (for display purposes).
        /// </summary>
        public MessageRole Role { get; set; }

        /// <summary>
        /// Name of the sender.
        /// </summary>
        public string Name { get; set; }

        public UIMessageViewModel()
        {
            MessengerMessageId = new MessageId(string.Empty);
            ParentModelId = ModelMessageId.Empty;
            SegmentIndex = 0;
            TextContent = string.Empty;
            MediaContent = [];
            IsSent = false;
            ActiveButtons = null;
            Role = MessageRole.eRoleSystem;
            Name = string.Empty;
        }

        public bool HasMedia => MediaContent.Count > 0;
        public bool HasImage => MediaContent.OfType<ImageContentItem>().Any();
        public bool HasAudio => MediaContent.OfType<AudioContentItem>().Any();

        /// <summary>
        /// Whether this message currently has active buttons.
        /// </summary>
        public bool IsButtonsActive => IsSent && ActiveButtons != null && ActiveButtons.Count > 0;
    }
}
