using ChatWithAI.Contracts;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ChatWithAI.Plugins.Windows.ScreenshotCapture
{
    /// <summary> Windows specific hot-key service. Detects *double* Ctrl-C / Ctrl-V presses without suppressing them for other apps. </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsHotKeyService : IChatCtrlCEventSource, IChatCtrlVEventSource, IDisposable
    {
        private readonly ILogger m_log;
        private readonly string m_adminUserId;

        private readonly Subject<EventChatCtrlCHotkey> m_ctrlCSubject = new();
        public IObservable<EventChatCtrlCHotkey> CtrlCActions => m_ctrlCSubject.AsObservable();

        private readonly Subject<EventChatCtrlVHotkey> m_ctrlVSubject = new();
        public IObservable<EventChatCtrlVHotkey> CtrlVActions => m_ctrlVSubject.AsObservable();

        private volatile bool m_disposed;
        private IntPtr m_hook = IntPtr.Zero;
        private readonly Thread? m_thread;
        private LowLevelKeyboardProc? m_proc;
        private uint m_threadId;

        private readonly Stopwatch m_sw = Stopwatch.StartNew();
        private long m_lastCtrlCTicks;
        private long m_lastCtrlVTicks;
        private const int DoubleTapMs = 500;
        private const int DebounceMs = 50;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const uint VK_CONTROL = 0x11;
        private const uint VK_C = 0x43;
        private const uint VK_V = 0x56;
        private const uint WM_QUIT = 0x0012;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        // P/Invoke
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern void PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        // --------------------------------------------------------------------------------------------------------------------
        // Ctor
        // --------------------------------------------------------------------------------------------------------------------
        public WindowsHotKeyService(string chatId, ILogger log)
        {
            m_log = log;
            m_adminUserId = chatId;

            m_thread = new Thread(MessageLoopThread)
            {
                IsBackground = true,
                Name = "WindowsHotKeyServiceThread"
            };
            m_thread.SetApartmentState(ApartmentState.STA);
            m_thread.Start();
        }

        // --------------------------------------------------------------------------------------------------------------------
        // Public API
        // --------------------------------------------------------------------------------------------------------------------
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_disposed = true;
                if (m_hook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(m_hook);
                    m_hook = IntPtr.Zero;
                }
                if (m_threadId != 0)
                {
                    PostThreadMessage(m_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                }
                m_thread?.Join(100);
                m_ctrlCSubject.OnCompleted();
                m_ctrlCSubject.Dispose();
                m_ctrlVSubject.OnCompleted();
                m_ctrlVSubject.Dispose();
            }
        }

        // --------------------------------------------------------------------------------------------------------------------
        // Implementation
        // --------------------------------------------------------------------------------------------------------------------
        private void MessageLoopThread()
        {
            m_threadId = GetCurrentThreadId();
            m_proc = HookCallback;
            m_hook = SetHook(m_proc);
            if (m_hook == IntPtr.Zero)
            {
                m_log.LogInfoMessage($"[HotKey] Failed to install hook. Win32Error={Marshal.GetLastWin32Error()}");
                return;
            }

            while (!m_disposed && GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Call the next hook in the chain immediately.
            IntPtr next = CallNextHookEx(m_hook, nCode, wParam, lParam);

            if (nCode < 0) return next; // Not HC_ACTION
            if (wParam != WM_KEYDOWN && wParam != WM_SYSKEYDOWN) return next;

            // Fast check: do not marshal unless the virtual key is of interest.
            uint vk = (uint)Marshal.ReadInt32(lParam); // vkCode is the first field in KBDLLHOOKSTRUCT
            if (vk != VK_C && vk != VK_V) return next;
            if ((GetAsyncKeyState((int)VK_CONTROL) & 0x8000) == 0) return next;

            // double-tap detection (lock-free)
            long now = m_sw.ElapsedMilliseconds;
            ref long last = ref (vk == VK_C ? ref m_lastCtrlCTicks : ref m_lastCtrlVTicks);
            long delta = now - Interlocked.Exchange(ref last, now);

            if (delta > DebounceMs && delta < DoubleTapMs)
            {
                if (vk == VK_C)
                {
                    m_log.LogInfoMessage("Double Ctrl+C detected.");
                    m_ctrlCSubject.OnNext(new EventChatCtrlCHotkey(m_adminUserId));
                }
                else // vk == VK_V
                {
                    m_log.LogInfoMessage("Double Ctrl+V detected.");
                    m_ctrlVSubject.OnNext(new EventChatCtrlVHotkey(m_adminUserId));
                }
            }

            return next;
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using Process curProcess = Process.GetCurrentProcess();
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            using ProcessModule curModule = curProcess.MainModule;
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curProcess.MainModule.ModuleName), 0);
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }
    }
}
