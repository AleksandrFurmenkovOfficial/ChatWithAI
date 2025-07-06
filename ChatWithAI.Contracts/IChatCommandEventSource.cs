using System;

namespace ChatWithAI.Contracts
{
    public interface IChatCommandEventSource
    {
        IObservable<EventChatCommand> ChatCommands { get; }
    }
}