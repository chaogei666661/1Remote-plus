using System;
using System.Globalization;
using System.Threading;
using _1RM.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils
{
    /// <summary>
    /// The names this app offers for the files it writes for the user.
    ///
    /// The JSON server export and the error report were built with <c>yyyyMMddhhmmss</c> — the twelve-hour
    /// clock, with no <c>tt</c> to say which half of the day it was. Two exports on the same day, one before
    /// noon and one after, were offered the same name.
    /// </summary>
    [TestClass]
    public class TimestampedFileNameTests
    {
        [TestInitialize]
        public void Setup() => TestInit.Init();

        /// <summary>
        /// The defect. 09:30 and 21:30 produced the same twelve-hour stamp, so accepting the suggested name
        /// twice in a day overwrote the morning's export with the evening's.
        /// </summary>
        [TestMethod]
        public void MorningAndEveningDoNotProduceTheSameName()
        {
            var morning = TimestampedFileName.For("1Remote-servers", ".json", new DateTime(2026, 8, 28, 9, 30, 0));
            var evening = TimestampedFileName.For("1Remote-servers", ".json", new DateTime(2026, 8, 28, 21, 30, 0));

            Assert.AreNotEqual(morning, evening);
            Assert.AreEqual("1Remote-servers-20260828-093000.json", morning);
            Assert.AreEqual("1Remote-servers-20260828-213000.json", evening);
        }

        /// <summary>
        /// A folder listing sorts by text and nothing else, so the stamp has to run from the largest unit to
        /// the smallest for the newest export to be the last one.
        /// </summary>
        [TestMethod]
        public void NamesSortIntoChronologicalOrderAsText()
        {
            var earlier = TimestampedFileName.For("x", "log", new DateTime(2026, 8, 28, 23, 59, 59));
            var later = TimestampedFileName.For("x", "log", new DateTime(2026, 8, 29, 0, 0, 0));

            Assert.IsTrue(string.CompareOrdinal(earlier, later) < 0);
        }

        [TestMethod]
        public void MidnightAndNoonAreTheHoursMostLikelyToBeWrong()
        {
            Assert.AreEqual("20260828-000000", TimestampedFileName.Stamp(new DateTime(2026, 8, 28, 0, 0, 0)));
            Assert.AreEqual("20260828-120000", TimestampedFileName.Stamp(new DateTime(2026, 8, 28, 12, 0, 0)));
            Assert.AreEqual("20260828-235959", TimestampedFileName.Stamp(new DateTime(2026, 8, 28, 23, 59, 59)));
        }

        /// <summary>
        /// A file name is an identifier. Under a Thai locale the current culture's calendar is Buddhist and
        /// the same moment formats as year 2569; under an Arabic (Saudi Arabia) one it is Hijri. Either
        /// would give the same export two unrelated names on two desktops in the same office.
        /// </summary>
        [TestMethod]
        public void TheYearIsGregorianWhateverCalendarTheDesktopIsSetTo()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("th-TH");
                Assert.AreEqual("20260828-093000", TimestampedFileName.Stamp(new DateTime(2026, 8, 28, 9, 30, 0)));

                Thread.CurrentThread.CurrentCulture = new CultureInfo("ar-SA");
                Assert.AreEqual("20260828-093000", TimestampedFileName.Stamp(new DateTime(2026, 8, 28, 9, 30, 0)));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [TestMethod]
        public void AnExtensionIsAcceptedWithOrWithoutItsDot()
        {
            var when = new DateTime(2026, 8, 28, 9, 30, 0);

            Assert.AreEqual("r-20260828-093000.csv", TimestampedFileName.For("r", ".csv", when));
            Assert.AreEqual("r-20260828-093000.csv", TimestampedFileName.For("r", "csv", when));
        }

        [TestMethod]
        public void WithNoPrefixTheNameIsJustTheStamp()
        {
            var when = new DateTime(2026, 8, 28, 9, 30, 0);

            Assert.AreEqual("20260828-093000.json", TimestampedFileName.For("", ".json", when));
            Assert.AreEqual("20260828-093000.json", TimestampedFileName.For("   ", ".json", when));
        }

        /// <summary>
        /// A trailing space in the prefix would survive into the file name, where Windows silently strips it
        /// and the caller ends up with a path that does not match what the dialog showed.
        /// </summary>
        [TestMethod]
        public void SurroundingSpaceInThePrefixDoesNotReachTheFileName()
        {
            Assert.AreEqual("report-20260828-093000.md",
                TimestampedFileName.For("  report  ", ".md", new DateTime(2026, 8, 28, 9, 30, 0)));
        }

        [TestMethod]
        public void WithNoExtensionTheNameStopsAtTheStamp()
        {
            Assert.AreEqual("dump-20260828-093000",
                TimestampedFileName.For("dump", "", new DateTime(2026, 8, 28, 9, 30, 0)));
        }
    }
}
