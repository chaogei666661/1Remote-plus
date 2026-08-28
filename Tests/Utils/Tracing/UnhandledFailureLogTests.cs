using System;
using System.Threading;
using System.Threading.Tasks;
using _1RM.Utils.Tracing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.Tracing
{
    /// <summary>
    /// The record a crash off the UI thread leaves behind.
    ///
    /// The Bootstrapper handles <c>DispatcherUnhandledException</c>, which is the WPF dispatcher and
    /// nothing else. The audit writer thread, the transfer threads, SSH.NET's receive threads, the
    /// retention pass and every <c>Task.Factory.StartNew</c> body are not on it, and an exception escaping
    /// one of those used to produce nothing at all — the process vanished, or a faulted Task was simply
    /// never looked at and the work silently did not happen.
    ///
    /// A real unhandled exception cannot be raised in-process without ending it, so the formatting and the
    /// flood limit are separated from the event wiring and checked here. Both are string and counter work,
    /// so Windows and Linux agree.
    /// </summary>
    [TestClass]
    public class UnhandledFailureLogTests
    {
        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
        }

        [TestMethod]
        public void AFailureIsWrittenWithItsOriginAndItsStack()
        {
            var log = new UnhandledFailureLog();

            var text = log.Describe(new InvalidOperationException("the queue is gone"), "AppDomain.UnhandledException", true);

            Assert.IsNotNull(text);
            StringAssert.Contains(text!, "AppDomain.UnhandledException");
            StringAssert.Contains(text!, "the queue is gone");
            StringAssert.Contains(text!, nameof(InvalidOperationException));
        }

        /// <summary>
        /// Whether the process is about to die is the difference between "this session is over" and "the
        /// app kept running with something silently not done", and the two need different reading.
        /// </summary>
        [TestMethod]
        public void TheTextSaysWhetherTheProcessIsAboutToDie()
        {
            var log = new UnhandledFailureLog();

            StringAssert.Contains(log.Describe(new Exception("x"), "w", true)!, "terminating");
            StringAssert.Contains(log.Describe(new Exception("x"), "w", false)!, "continues");
        }

        [TestMethod]
        public void TheThreadIsNamedBecauseThatIsThePointOfTheWholeThing()
        {
            var log = new UnhandledFailureLog();

            var text = log.Describe(new Exception("x"), "w", false);

            StringAssert.Contains(text!, "thread " + Environment.CurrentManagedThreadId);
        }

        /// <summary>
        /// <c>UnhandledExceptionEventArgs.ExceptionObject</c> is typed <c>object</c>: the CLR permits a
        /// throw of anything, and a null has been seen during shutdown. A crash handler that casts is a
        /// crash handler that crashes.
        /// </summary>
        [TestMethod]
        public void SomethingThatIsNotAnExceptionIsStillDescribed()
        {
            var log = new UnhandledFailureLog();

            var text = log.Describe("just a string", "w", true);

            Assert.IsNotNull(text);
            StringAssert.Contains(text!, "non-exception");
            StringAssert.Contains(text!, "just a string");
        }

        [TestMethod]
        public void NothingAtAllIsStillDescribed()
        {
            var log = new UnhandledFailureLog();

            var text = log.Describe(null, "w", true);

            Assert.IsNotNull(text);
            StringAssert.Contains(text!, "no exception object");
        }

        private sealed class Unprintable
        {
            public override string ToString() => throw new NotSupportedException("no");
        }

        [TestMethod]
        public void AnObjectWhoseToStringThrowsDoesNotTakeTheHandlerWithIt()
        {
            var log = new UnhandledFailureLog();

            var text = log.Describe(new Unprintable(), "w", true);

            Assert.IsNotNull(text);
            StringAssert.Contains(text!, nameof(NotSupportedException));
        }

        /// <summary>
        /// A background loop that throws on every iteration would otherwise write the same stack trace
        /// until the disk fills — and the user's database is on that disk.
        /// </summary>
        [TestMethod]
        public void TheLogStopsAfterItsLimitSoALoopCannotFillTheDisk()
        {
            var log = new UnhandledFailureLog(3);

            Assert.IsNotNull(log.Describe(new Exception("1"), "w", false));
            Assert.IsNotNull(log.Describe(new Exception("2"), "w", false));
            var last = log.Describe(new Exception("3"), "w", false);
            Assert.IsNotNull(last);
            Assert.IsNull(log.Describe(new Exception("4"), "w", false));
            Assert.IsNull(log.Describe(new Exception("5"), "w", false));

            Assert.AreEqual(5, log.Seen, "the ones that were dropped are still counted");
        }

        /// <summary>
        /// The line that stops has to say it stopped, or a log ending after twenty entries reads as an app
        /// that recovered.
        /// </summary>
        [TestMethod]
        public void TheLastLineWrittenSaysThatItIsTheLastOne()
        {
            var log = new UnhandledFailureLog(2);

            Assert.IsFalse(log.Describe(new Exception("1"), "w", false)!.Contains("limit"));
            StringAssert.Contains(log.Describe(new Exception("2"), "w", false)!, "limit");
        }

        [TestMethod]
        public void ALimitBelowOneIsReadAsOne()
        {
            var log = new UnhandledFailureLog(0);

            Assert.AreEqual(1, log.Limit);
            Assert.IsNotNull(log.Describe(new Exception("1"), "w", false));
            Assert.IsNull(log.Describe(new Exception("2"), "w", false));
        }

        /// <summary>
        /// The hooks fire on whichever thread failed, and several can fail at once. The count has to be
        /// the number of calls, not whatever two racing increments left behind.
        /// </summary>
        [TestMethod]
        public void TheLimitHoldsWhenSeveralThreadsFailAtOnce()
        {
            const int calls = 20000;
            var log = new UnhandledFailureLog(10);
            var written = 0;

            Parallel.For(0, calls,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount) },
                _ =>
                {
                    if (log.Describe(new Exception("x"), "w", false) != null)
                        Interlocked.Increment(ref written);
                });

            Assert.AreEqual(10, written);
            Assert.AreEqual(calls, log.Seen, "an increment was lost, so two threads were handed the same slot");
        }

        /// <summary>
        /// The tracer takes an <see cref="Exception"/>, and the hook may not have one. Manufacturing a
        /// stand-in keeps the report from being dropped for a type mismatch.
        /// </summary>
        [TestMethod]
        public void AnExceptionIsHandedBackUnchangedForTheTracer()
        {
            var original = new InvalidOperationException("keep me");

            Assert.AreSame(original, UnhandledFailureLog.AsException(original, "w"));
        }

        [TestMethod]
        public void SomethingThatIsNotAnExceptionBecomesOneRatherThanNull()
        {
            var made = UnhandledFailureLog.AsException("a string", "AppDomain.UnhandledException");

            Assert.IsNotNull(made);
            StringAssert.Contains(made.Message, "AppDomain.UnhandledException");
            StringAssert.Contains(made.Message, "System.String");

            var fromNothing = UnhandledFailureLog.AsException(null, "w");
            Assert.IsNotNull(fromNothing);
            StringAssert.Contains(fromNothing.Message, "no exception object");
        }

        /// <summary>
        /// An <see cref="AggregateException"/> is what <c>TaskScheduler.UnobservedTaskException</c>
        /// carries, and the exception that actually happened is inside it. Losing it to the wrapper's own
        /// unhelpful message would make the whole hook pointless.
        /// </summary>
        [TestMethod]
        public void TheRealFailureInsideAnAggregateStillReachesTheLog()
        {
            var log = new UnhandledFailureLog();
            var aggregate = new AggregateException(new TimeoutException("the SFTP listing never answered"));

            var text = log.Describe(aggregate, "TaskScheduler.UnobservedTaskException", false);

            StringAssert.Contains(text!, "the SFTP listing never answered");
        }
    }
}
