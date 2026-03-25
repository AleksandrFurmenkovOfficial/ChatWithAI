using System;

namespace ChatWithAI.Contracts
{
    public interface IChatCtrlCEventSource
    {
        IObservable<EventChatCtrlCHotkey> CtrlCActions { get; }
    }
}