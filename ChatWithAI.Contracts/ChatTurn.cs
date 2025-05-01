using System.Collections.Generic;

namespace ChatWithAI.Contracts
{
    public class ChatTurn : List<ChatMessage>
    {
        public ChatTurn() : base() { }

        public ChatTurn(IEnumerable<ChatMessage> collection) : base(collection) { }
    }
}
