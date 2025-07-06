using System;

namespace ChatWithAI.Contracts
{
    public interface IChatMessageEventSource
    {
        IObservable<EventChatMessage> ChatMessages { get; }
    }
}