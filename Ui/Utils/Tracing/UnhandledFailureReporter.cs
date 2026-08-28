using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Shawn.Utils;

namespace _1RM.Utils.Tracing
{
    /// <summary>
    /// Subscribes the two failure hooks WPF does not give you.
    ///
    /// <c>Bootstrapper.OnUnhandledException</c> covers the dispatcher thread and nothing else. See
    /// <see cref="UnhandledFailureLog"/> for what that leaves out and why it matters here.
    ///
    /// Neither hook can prevent anything: <see cref="AppDomain.UnhandledException"/> fires while the
    /// runtime is already ending the process, and this is not the place to put a dialog on the screen —
    /// the UI thread may be the one that died. The value is that the failure is written down at all. A
    /// support case that starts "it just disappears" is a different case from one with a stack trace in
    /// <c>.logs/1Remote.log.md</c>.
    /// </summary>
    internal static class UnhandledFailureReporter
    {
        private static readonly UnhandledFailureLog Log = new UnhandledFailureLog();
        private static int _installed;

        /// <summary>
        /// Idempotent, and called before anything else starts a thread. Safe to call from
        /// <c>AppInitHelper.Init</c>, which is the first line of <c>Main</c>.
        /// </summary>
        public static void Install()
        {
            if (Interlocked.Exchange(ref _installed, 1) != 0)
                return;

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Report(e.ExceptionObject, "AppDomain.UnhandledException", e.IsTerminating);

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                // A faulted Task nobody awaited does not end the process on .NET Core the way it did on
                // .NET Framework — it is simply invisible, which is how a transfer can report success and
                // move nothing. Observing it keeps that true regardless of how the host is configured.
                Report(e.Exception, "TaskScheduler.UnobservedTaskException", false);
                e.SetObserved();
            };
        }

        private static void Report(object? exceptionObject, string where, bool isTerminating)
        {
            // Nothing in here may throw. An exception raised inside AppDomain.UnhandledException is not
            // caught by anything at all.
            try
            {
                var text = Log.Describe(exceptionObject, where, isTerminating);
                if (text == null)
                    return;

                if (isTerminating)
                    SimpleLogHelper.Fatal(text);
                else
                    SimpleLogHelper.Error(text);

                UnifyTracing.Error(UnhandledFailureLog.AsException(exceptionObject, where), new Dictionary<string, string>
                {
                    { "Where", where },
                    { "IsTerminating", isTerminating.ToString() },
                });
            }
            catch (Exception)
            {
                // ignored, deliberately: there is no safer place left to report from
            }
        }
    }
}
