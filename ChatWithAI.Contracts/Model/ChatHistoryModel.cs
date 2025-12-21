using System;
using System.Collections.Generic;
using System.Linq;

namespace ChatWithAI.Contracts.Model
{
    /// <summary>
    /// Pure domain model for chat history.
    /// Contains the complete conversation history for AI context.
    /// No UI-specific state.
    /// </summary>
    public sealed class ChatHistoryModel
    {
        public string ChatId { get; }
        public List<ChatTurnModel> Turns { get; }

        public ChatHistoryModel(string chatId)
        {
            ChatId = chatId;
            Turns = [];
        }

        public ChatHistoryModel(string chatId, List<ChatTurnModel> turns)
        {
            ChatId = chatId;
            Turns = turns;
        }

        /// <summary>
        /// Adds user message(s) to the history.
        /// Creates a new turn or adds to existing turn based on the last message role.
        /// </summary>
        public void AddUserMessages(List<ChatMessageModel> messages, bool forceAddToLastTurn = false)
        {
            if (messages.Count == 0) return;

            if (Turns.Count > 0 && Turns[^1].Count > 0)
            {
                var lastMessage = Turns[^1][^1];
                if (lastMessage.Role == MessageRole.eRoleUser || forceAddToLastTurn)
                {
                    // Add to existing turn
                    var lastTurn = Turns[^1];
                    lastTurn.AddRange(messages);
                    // Sort by creation time
                    lastTurn.Sort((m1, m2) =>
                    {
                        if (m1.OriginalMessageId.HasValue && m2.OriginalMessageId.HasValue)
                        {
                            return m1.OriginalMessageId.Value.CompareTo(m2.OriginalMessageId.Value);
                        }
                        return m1.CreatedAt.CompareTo(m2.CreatedAt);
                    });
                    return;
                }
            }

            // Create new turn
            Turns.Add([.. messages]);
        }

        /// <summary>
        /// Adds an assistant message to the last turn.
        /// </summary>
        public void AddAssistantMessage(ChatMessageModel message)
        {
            if (Turns.Count == 0)
            {
                throw new InvalidOperationException("Cannot add assistant message without a preceding user message.");
            }
            Turns[^1].Add(message);
        }

        /// <summary>
        /// Gets the last assistant message from history.
        /// </summary>
        public ChatMessageModel? GetLastAssistantMessage()
        {
            if (Turns.Count == 0) return null;
            return Turns[^1].GetLastAssistantMessage();
        }

        /// <summary>
        /// Removes all assistant messages from the last turn.
        /// Returns the removed messages.
        /// </summary>
        public List<ChatMessageModel> RemoveAllAssistantMessagesFromLastTurn()
        {
            if (Turns.Count == 0) return [];

            var lastTurn = Turns[^1];
            var removed = lastTurn.RemoveAllAssistantMessages();

            // Remove empty turn
            if (lastTurn.Count == 0)
            {
                Turns.RemoveAt(Turns.Count - 1);
            }

            return removed;
        }

        /// <summary>
        /// Removes a specific message from the last turn.
        /// </summary>
        public bool RemoveMessageFromLastTurn(ChatMessageModel message)
        {
            if (Turns.Count == 0) return false;

            var lastTurn = Turns[^1];
            bool removed = lastTurn.Remove(message);

            if (lastTurn.Count == 0)
            {
                Turns.RemoveAt(Turns.Count - 1);
            }

            return removed;
        }

        /// <summary>
        /// Gets all messages as a flat list for AI context.
        /// </summary>
        public List<ChatMessageModel> GetAllMessagesForAI()
        {
            return Turns.SelectMany(turn => turn.Select(msg => msg.Clone())).ToList();
        }

        /// <summary>
        /// Clears all history.
        /// </summary>
        public void Clear()
        {
            Turns.Clear();
        }

        public void UpdateMessageOriginalId(ModelMessageId id, long originalId)
        {
            foreach (var turn in Turns)
            {
                var msg = turn.FirstOrDefault(m => m.Id == id);
                if (msg != null)
                {
                    msg.OriginalMessageId = originalId;
                    return;
                }
            }
        }

        public bool IsEmpty => Turns.Count == 0;
    }
}
