using ChatWithAI.Contracts;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace ChatWithAI.Plugins.Windows.ScreenshotCapture
{
    public sealed class WindowsHotKeyServiceStub() : IChatCtrlCEventSource, IChatCtrlVEventSource, IDisposable
    {
        private readonly Subject<EventChatCtrlCHotkey> m_ctrlCSubject = new();
        public IObservable<EventChatCtrlCHotkey> CtrlCActions => m_ctrlCSubject.AsObservable();

        private readonly Subject<EventChatCtrlVHotkey> m_ctrlVSubject = new();
        public IObservable<EventChatCtrlVHotkey> CtrlVActions => m_ctrlVSubject.AsObservable();

        public void Dispose()
        {
        }
    }
}
