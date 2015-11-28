using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MiniMagellan
{
    // todo will need special break handling for mono
#if false
    public interface IExitSignal
    {
        event EventHandler Exit;
    }

#if __MonoCS__
    public class UnixExitSignal : IExitSignal
    {
        public event EventHandler Exit;
        UnixSignal[] signals = new UnixSignal[]{
        new UnixSignal(Mono.Unix.Native.Signum.SIGTERM),
        new UnixSignal(Mono.Unix.Native.Signum.SIGINT),
        new UnixSignal(Mono.Unix.Native.Signum.SIGUSR1)
    };

        public UnixExitSignal()
        {
            Task.Factory.StartNew(() =>
            {
            // blocking call to wait for any kill signal
            int index = UnixSignal.WaitAny(signals, -1);
                if (Exit != null)
                    Exit(null, EventArgs.Empty);
            });
        }
       }
#endif

    public class WinExitSignal : IExitSignal
    {
        public event EventHandler Exit;

        [DllImport("Kernel32")]
        public static extern bool SetConsoleCtrlHandler(HandlerRoutine Handler, bool Add);

        public delegate bool HandlerRoutine(CtrlTypes CtrlType);

        public enum CtrlTypes
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT,
            CTRL_CLOSE_EVENT,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT
        }

        private HandlerRoutine m_hr;

        public WinExitSignal()
        {
            m_hr = new HandlerRoutine(ConsoleCtrlCheck);
            SetConsoleCtrlHandler(m_hr, true);
        }

        private bool ConsoleCtrlCheck(CtrlTypes ctrlType)
        {
            switch (ctrlType)
            {
                case CtrlTypes.CTRL_C_EVENT:
                case CtrlTypes.CTRL_BREAK_EVENT:
                case CtrlTypes.CTRL_CLOSE_EVENT:
                case CtrlTypes.CTRL_LOGOFF_EVENT:
                case CtrlTypes.CTRL_SHUTDOWN_EVENT:
                    if (Exit != null)
                        Exit(this, EventArgs.Empty);
                    break;
                default:
                    break;
            }
            return true;
        }
    }
#endif
}
