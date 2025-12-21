using System;
using System.Threading.Tasks;

namespace ChatWithAI.Contracts
{
    public interface IChatActionEventSource
    {
        IObservable<EventChatAction> ChatActions { get; }
        Task Run();
    }
}