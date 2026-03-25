using System;

namespace ChatWithAI.Contracts
{
    public interface IChatExpireEventSource
    {
        IObservable<EventChatExpire> ExpireChats { get; }
    }
}