using System;

namespace ChatWithAI.Contracts
{
    public interface IChatCtrlVEventSource
    {
        IObservable<EventChatCtrlVHotkey> CtrlVActions { get; }
    }
}