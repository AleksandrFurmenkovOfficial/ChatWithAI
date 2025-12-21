using System.Collections.Generic;

namespace ChatWithAI.Contracts.Model
{
    /// <summary>
    /// Represents a single turn in the conversation.
    /// A turn typically consists of user message(s) followed by AI response(s).
    /// </summary>
    public sealed class ChatTurnModel : List<ChatMessageModel>
    {
        public ChatTurnModel() : base() { }

        public ChatTurnModel(IEnumerable<ChatMessageModel> collection) : base(collection) { }

        public ChatMessageModel? GetLastUserMessage()
        {
            for (int i = Count - 1; i >= 0; i--)
            {
                if (this[i].Role == MessageRole.eRoleUser)
                    return this[i];
            }
            return null;
        }

        public ChatMessageModel? GetLastAssistantMessage()
        {
            for (int i = Count - 1; i >= 0; i--)
            {
                if (this[i].Role == MessageRole.eRoleAI)
                    return this[i];
            }
            return null;
        }

        public List<ChatMessageModel> RemoveAllAssistantMessages()
        {
            var removed = new List<ChatMessageModel>();
            for (int i = Count - 1; i >= 0; i--)
            {
                if (this[i].Role != MessageRole.eRoleUser)
                {
                    removed.Add(this[i]);
                    RemoveAt(i);
                }
            }
            return removed;
        }

        public ChatTurnModel Clone()
        {
            var cloned = new ChatTurnModel();
            foreach (var msg in this)
            {
                cloned.Add(msg.Clone());
            }
            return cloned;
        }
    }
}
