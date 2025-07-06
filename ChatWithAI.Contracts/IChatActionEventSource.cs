using System;

namespace ChatWithAI.Contracts
{
    public interface IChatActionEventSource
    {
        IObservable<EventChatAction> ChatActions { get; }
    }
}