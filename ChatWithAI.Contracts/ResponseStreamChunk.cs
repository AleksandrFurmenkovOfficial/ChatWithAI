using System.Collections.Generic;

namespace ChatWithAI.Contracts
{
    public class ResponseStreamChunk
    {
        public List<ChatMessage> Messages { get; }

        public string? TextDelta { get; }

        public ResponseStreamChunk(string textDelta) : this(null, textDelta)
        {
            TextDelta = textDelta;
        }

        public ResponseStreamChunk(IEnumerable<ChatMessage>? messages, string? textDelta = null)
        {
            TextDelta = textDelta;
            Messages = [];

            if (messages != null)
            {
                Messages.AddRange(messages);
            }
        }
    }

    public sealed class LastResponseStreamChunk : ResponseStreamChunk
    {
        public LastResponseStreamChunk() : base(null, string.Empty) { }
    }
}