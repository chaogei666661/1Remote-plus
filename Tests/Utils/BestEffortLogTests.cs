using System;
using System.Globalization;
using _1RM.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shawn.Utils;

namespace Tests.Utils
{
    /// <summary>
    /// <see cref="BestEffortLog"/> is one try/catch, which is not much to test - but the reason it exists
    /// is easy to lose, and these cases are what says a log line is not allowed to decide whether the work
    /// around it succeeded.
    /// </summary>
    [TestClass]
    public class BestEffortLogTests
    {
        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
        }

        [TestMethod]
        public void TheLogCallIsActuallyMade()
        {
            var written = 0;

            BestEffortLog.Write(() => ++written);

            Assert.AreEqual(1, written, "swallowing the exception is no use if the line never gets written either");
        }

        [TestMethod]
        public void ALoggerThatThrowsDoesNotReachTheCaller()
        {
            // The real one throws ArgumentOutOfRangeException out of a Substring; anything at all is caught,
            // because there is no shortlist of ways a logger is allowed to fail.
            BestEffortLog.Write(() => throw new ArgumentOutOfRangeException("startIndex"));
            BestEffortLog.Write(() => throw new InvalidOperationException());
        }

        /// <summary>
        /// The case this class was written for. <c>SimpleLogHelper</c> cuts the directory off the calling
        /// frame's source path with the culture-sensitive <c>LastIndexOf("\\")</c>; ICU's Thai collation
        /// treats a backslash as ignorable and answers the length of the string, so on Windows - where the
        /// path is full of backslashes - the slice that follows throws for every log call made under
        /// <c>th-TH</c>. On Linux the frame's path has no backslash in it and the throw never happens, so
        /// this case is a real guard on one platform and a plain "it still logs" on the other.
        /// </summary>
        [TestMethod]
        public void AThaiDesktopCanStillLogALine()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("th-TH");

                BestEffortLog.Write(() => SimpleLogHelper.Info("BestEffortLogTests: a line written under th-TH"));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }
}
