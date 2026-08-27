using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Shawn.Utils;

namespace _1RM.Service
{
    /// <summary>
    /// The last resort for a shutdown that never finishes.
    ///
    /// Quitting has to tear down things this app does not fully own — the ActiveX RDP control, PuTTY and
    /// mstsc child processes, the tray icon, SSH tunnels — and any one of them can leave a foreground thread
    /// behind, which keeps the process alive with no window to close it from. The user then cannot start the
    /// app again, because the next launch finds the named pipe of the old one and quietly exits.
    ///
    /// So the failsafe stays. What it does now is say what was still running before it pulls the plug,
    /// because the previous version left no trace at all: a hung shutdown looked exactly like a clean one
    /// that happened to take five seconds, and the exit code was always 1 even when the user had simply
    /// chosen Quit.
    /// </summary>
    internal static class ShutdownWatchdog
    {
        /// <summary>Long enough for a normal teardown, short enough that a stuck one is not left sitting.</summary>
        private const int GRACE_SECONDS = 5;

        private static int _armed;

        /// <summary>
        /// True once the user asked to quit through <see cref="App.Close"/>. A shutdown that starts anywhere
        /// else — an unhandled exception, the session ending, the container being torn down — is not clean,
        /// and the exit code should say so.
        /// </summary>
        public static bool IsCleanShutdown { get; private set; }

        public static void RequestClean() => IsCleanShutdown = true;

        /// <summary>
        /// Starts the countdown. Calling it twice is harmless: the first caller owns the timer, so quitting
        /// through App.Close and then falling into Bootstrapper.OnExit does not give the teardown two
        /// separate deadlines.
        /// </summary>
        /// <param name="exitCode">What to exit with if the shutdown really does hang.</param>
        public static void Arm(int exitCode)
        {
            if (Interlocked.Exchange(ref _armed, 1) != 0)
                return;

            // A background thread, so this timer is not itself a reason for the process to stay up: if the
            // shutdown finishes in time nobody has to cancel anything.
            var thread = new Thread(() =>
            {
                Thread.Sleep(GRACE_SECONDS * 1000);
                var code = IsCleanShutdown ? exitCode : 1;
                LogWhatIsStillRunning(code);
                Environment.Exit(code);
            })
            {
                IsBackground = true,
                Name = "ShutdownWatchdog",
            };
            thread.Start();
        }

        private static void LogWhatIsStillRunning(int exitCode)
        {
            try
            {
                var clean = IsCleanShutdown ? "requested" : "not requested";
                SimpleLogHelper.Warning($"ShutdownWatchdog: still alive {GRACE_SECONDS}s after a {clean} shutdown, exiting with {exitCode}.");

                var sessions = IoC.TryGet<SessionControlService>()?.ConnectionId2Hosts;
                if (sessions?.IsEmpty == false)
                    SimpleLogHelper.Warning($"ShutdownWatchdog: {sessions.Count} session(s) not released: {string.Join(", ", sessions.Keys)}");

                SimpleLogHelper.Warning($"ShutdownWatchdog: {Process.GetCurrentProcess().Threads.Count} OS thread(s) in the process.");

                // Through the dispatcher, because Windows may only be read from the thread that owns it, and
                // with a deadline, because a UI thread that never answers is the most likely reason we are
                // here in the first place — and is worth reporting on its own.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null) return;
                try
                {
                    var titles = dispatcher.Invoke(
                        () => Application.Current.Windows.OfType<Window>().Select(x => $"{x.GetType().Name}('{x.Title}')").ToArray(),
                        DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(1));
                    if (titles.Length > 0)
                        SimpleLogHelper.Warning($"ShutdownWatchdog: {titles.Length} window(s) still open: {string.Join(", ", titles)}");
                }
                catch (TimeoutException)
                {
                    SimpleLogHelper.Warning("ShutdownWatchdog: the UI thread did not answer within a second, it is stuck.");
                }
            }
            catch (Exception e)
            {
                // Nothing here may stop the exit: this runs precisely when the app is already misbehaving.
                SimpleLogHelper.Warning($"ShutdownWatchdog: could not report what was left over, {e.Message}");
            }
        }
    }
}
