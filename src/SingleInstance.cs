using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace QuotaPeek.Windows
{
    /// <summary>
    /// Keeps one QuotaPeek per user session. A second launch does not start another copy; it asks the running
    /// one to show its quota flyout and exits.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        public const string DefaultName = @"Local\QuotaPeek.Windows";
        private const int AllowAnyProcess = -1;

        private readonly string _showEventName;
        private readonly Mutex _mutex;
        private readonly EventWaitHandle _showEvent;
        private RegisteredWaitHandle _registration;

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

        public SingleInstance(string name = DefaultName)
        {
            _showEventName = name + ".Show";
            bool created;
            _mutex = new Mutex(true, name, out created);
            IsPrimary = created;
            if (IsPrimary) _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _showEventName);
        }

        public bool IsPrimary { get; private set; }

        /// <summary>Runs <paramref name="onShowRequested"/> on a worker thread whenever another launch asks to show.</summary>
        public void ListenForShowRequests(Action onShowRequested)
        {
            if (!IsPrimary || _registration != null) return;
            _registration = ThreadPool.RegisterWaitForSingleObject(_showEvent,
                delegate { onShowRequested(); }, null, Timeout.Infinite, false);
        }

        /// <summary>Asks the running instance to show its flyout. Returns false if it cannot be reached.</summary>
        public bool SignalPrimary()
        {
            if (IsPrimary) return false;
            try
            {
                EventWaitHandle showEvent;
                if (!EventWaitHandle.TryOpenExisting(_showEventName, out showEvent)) return false;
                using (showEvent)
                {
                    // This launch came from the user, so it may hand foreground rights to the running instance;
                    // otherwise Windows could keep the flyout from taking focus.
                    AllowSetForegroundWindow(AllowAnyProcess);
                    return showEvent.Set();
                }
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_registration != null) _registration.Unregister(null);
            if (_showEvent != null) _showEvent.Dispose();
            if (IsPrimary)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Released from a different thread than the owner; disposing still frees the handle.
                }
            }
            _mutex.Dispose();
        }
    }
}
