using System;
using System.Globalization;
using System.Text;
using System.Threading;

namespace _1RM.Utils.Tracing
{
    /// <summary>
    /// What gets written when something throws where nobody was catching.
    ///
    /// <see cref="Bootstrapper"/> handles <c>DispatcherUnhandledException</c>, which is only the WPF
    /// dispatcher thread. Everything else this app runs is not on it: the audit writer thread, the SFTP and
    /// FTP transfer threads, SSH.NET's receive threads, the retention pass, the reachability timer, and
    /// every <c>Task.Factory.StartNew</c> body — of which the import and export paths alone have five. An
    /// exception escaping one of those produced no log line, no report and no dialog. The process either
    /// died without a word (<see cref="AppDomain.UnhandledException"/>) or, for a faulted
    /// <see cref="System.Threading.Tasks.Task"/> nobody awaited, carried on with the work silently not
    /// done. Two rounds of this fork have chased exactly that shape: a transfer that reported success and
    /// sent nothing, and a finaliser that would have taken the process with it.
    ///
    /// The formatting and the flood limit live here, apart from the event wiring, so both can be checked
    /// without raising a real unhandled exception — which by definition cannot be done in-process.
    /// </summary>
    public sealed class UnhandledFailureLog
    {
        /// <summary>
        /// How many failures are written before the log stops. A background loop that throws on every
        /// iteration would otherwise fill the disk with the same stack trace, and the disk is where the
        /// user's database lives.
        /// </summary>
        public const int DefaultLimit = 20;

        private int _seen;

        public UnhandledFailureLog(int limit = DefaultLimit)
        {
            Limit = limit < 1 ? 1 : limit;
        }

        public int Limit { get; }

        /// <summary>How many failures have been offered, including the ones past <see cref="Limit"/>.</summary>
        public int Seen => Volatile.Read(ref _seen);

        /// <summary>
        /// The text for one failure, or null once <see cref="Limit"/> has been passed.
        /// </summary>
        /// <param name="exceptionObject">
        /// What was thrown. <see cref="UnhandledExceptionEventArgs.ExceptionObject"/> is typed
        /// <see cref="object"/> and is not required to be an <see cref="Exception"/> — the CLR allows a
        /// throw of anything, and a null has been observed during shutdown — so this takes the untyped
        /// thing and says what it was rather than casting and throwing inside the crash handler.
        /// </param>
        /// <param name="where">Which hook this came from.</param>
        /// <param name="isTerminating">Whether the runtime is about to end the process.</param>
        public string? Describe(object? exceptionObject, string where, bool isTerminating)
        {
            var n = Interlocked.Increment(ref _seen);
            if (n > Limit)
                return null;

            var builder = new StringBuilder();
            builder.Append("Unhandled failure [")
                   .Append(string.IsNullOrWhiteSpace(where) ? "?" : where)
                   .Append(isTerminating ? ", the process is terminating" : ", the process continues")
                   .Append(", thread ")
                   .Append(Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture))
                   .Append(']');

            builder.Append(Environment.NewLine).Append(Detail(exceptionObject));

            if (n == Limit)
                builder.Append(Environment.NewLine)
                       .Append($"(the {Limit} unhandled-failure limit is reached; further ones will not be logged)");

            return builder.ToString();
        }

        private static string Detail(object? exceptionObject)
        {
            switch (exceptionObject)
            {
                case null:
                    return "nothing was attached to the event; the runtime reported no exception object";
                case Exception e:
                    return e.ToString();
                default:
                    // A throw of a non-Exception cannot be written in C#, but the CLR permits it and other
                    // languages emit it. ToString() on an arbitrary object can itself throw.
                    return $"a non-exception object of type {exceptionObject.GetType().FullName} was thrown: {SafeToString(exceptionObject)}";
            }
        }

        private static string SafeToString(object value)
        {
            try
            {
                return value.ToString() ?? "";
            }
            catch (Exception e)
            {
                return $"<its ToString() threw {e.GetType().Name}>";
            }
        }

        /// <summary>
        /// The same thing as an <see cref="Exception"/>, for a tracer that only accepts one. Never null,
        /// and never the caller's problem to null-check inside a crash handler.
        /// </summary>
        public static Exception AsException(object? exceptionObject, string where)
        {
            if (exceptionObject is Exception e)
                return e;
            return new InvalidOperationException(
                $"{where}: {(exceptionObject == null ? "no exception object" : "a non-exception object of type " + exceptionObject.GetType().FullName)} was thrown");
        }
    }
}
