using System;
using _1RM.Utils.FileTransmit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.FileTransmit
{
    /// <summary>
    /// A transfer scan refuses to queue the same source/destination pair twice. It used to decide that with
    /// StringComparison.CurrentCultureIgnoreCase, which is a linguistic comparison and not a textual one, so
    /// it called distinct file names equal and dropped the second from the transfer without a word.
    ///
    /// The cases below are the names that actually collided. They are asserted as *different* — that is the
    /// whole point — and every one of them fails if the comparer goes back to a culture-sensitive one.
    /// </summary>
    [TestClass]
    public class TransmitItemKeySetTests
    {
        private TransmitItemKeySet _set = new TransmitItemKeySet();

        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
            _set = new TransmitItemKeySet();
        }

        private void AssertBothQueue(string firstName, string secondName)
        {
            Assert.IsTrue(_set.Add("/srv/" + firstName, @"C:\dl\" + firstName));
            Assert.IsTrue(_set.Add("/srv/" + secondName, @"C:\dl\" + secondName),
                "these are two different files on the server and both have to be transferred");
            Assert.AreEqual(2, _set.Count);
        }

        [TestMethod]
        public void TheSamePairIsOnlyQueuedOnce()
        {
            Assert.IsTrue(_set.Add("/srv/a.txt", @"C:\dl\a.txt"));
            Assert.IsFalse(_set.Add("/srv/a.txt", @"C:\dl\a.txt"));
            Assert.AreEqual(1, _set.Count);
        }

        [TestMethod]
        public void TheSameSourceToADifferentDestinationIsNotADuplicate()
        {
            Assert.IsTrue(_set.Add("/srv/a.txt", @"C:\one\a.txt"));
            Assert.IsTrue(_set.Add("/srv/a.txt", @"C:\two\a.txt"));
        }

        [TestMethod]
        public void ADifferentSourceToTheSameDestinationIsNotADuplicate()
        {
            Assert.IsTrue(_set.Add("/srv/one/a.txt", @"C:\dl\a.txt"));
            Assert.IsTrue(_set.Add("/srv/two/a.txt", @"C:\dl\a.txt"));
        }

        /// <summary>
        /// The part of the old behaviour that was right: a Windows path differing only in case names the
        /// same file, so queueing it twice would transfer the same bytes twice.
        /// </summary>
        [TestMethod]
        public void APathThatDiffersOnlyInCaseIsStillADuplicate()
        {
            Assert.IsTrue(_set.Add(@"C:\Projects\A.txt", "/srv/up/A.txt"));
            Assert.IsFalse(_set.Add(@"c:\projects\a.txt", "/srv/up/a.txt"));
            Assert.AreEqual(1, _set.Count);
        }

        /// <summary>
        /// U+FB01 LATIN SMALL LIGATURE FI. A linguistic comparison reads it as "fi" and calls the two names
        /// the same word; they are two files.
        /// </summary>
        [TestMethod]
        public void ALigatureIsNotTheLettersItStandsFor()
        {
            AssertBothQueue("file.txt", "\uFB01le.txt");
        }

        /// <summary>
        /// The one that turns up without anybody trying: macOS writes decomposed names, so a server holding
        /// files from a Mac and from a PC has both spellings of the same accent side by side.
        /// </summary>
        [TestMethod]
        public void AComposedAccentIsNotItsDecomposedSpelling()
        {
            AssertBothQueue("caf\u00E9.txt", "cafe\u0301.txt");
        }

        [TestMethod]
        public void AZeroWidthSpaceIsNotNothing()
        {
            AssertBothQueue("note.txt", "note\u200B.txt");
        }

        [TestMethod]
        public void ASoftHyphenIsNotNothing()
        {
            AssertBothQueue("note.txt", "note\u00AD.txt");
        }

        [TestMethod]
        public void AnEmptyPathIsHandledRatherThanThrown()
        {
            Assert.IsTrue(_set.Add("", ""));
            Assert.IsFalse(_set.Add("", ""));
            Assert.IsTrue(_set.Add("/srv/a", ""));
        }

        [TestMethod]
        public void ContainsAnswersWithoutRecording()
        {
            Assert.IsFalse(_set.Contains("/srv/a.txt", @"C:\dl\a.txt"));
            _set.Add("/srv/a.txt", @"C:\dl\a.txt");
            Assert.IsTrue(_set.Contains("/srv/a.txt", @"C:\dl\a.txt"));
            Assert.AreEqual(1, _set.Count);
        }

        /// <summary>
        /// A POSIX file name may contain a newline, so the two paths cannot be joined into one key: with a
        /// separator of any kind, one pair's key can be spelled by a different pair, and the second file
        /// disappears from the transfer exactly the way the linguistic comparison made it disappear.
        /// </summary>
        [TestMethod]
        public void TwoPairsThatWouldShareAJoinedKeyAreStillTwoPairs()
        {
            Assert.IsTrue(_set.Add("/srv/a\nb", "c"));
            Assert.IsTrue(_set.Add("/srv/a", "b\nc"));
            Assert.AreEqual(2, _set.Count);
        }

        /// <summary>
        /// The reason the linear scan had to go: it was one linguistic comparison of two paths per item
        /// already queued, per item. A tree of this size is an ordinary source checkout.
        /// </summary>
        [TestMethod]
        public void TwentyThousandItemsAreQueuedWithoutTheScanStalling()
        {
            var started = DateTime.UtcNow;
            for (var i = 0; i < 20000; i++)
                Assert.IsTrue(_set.Add($@"C:\repo\src\m{i / 100}\f{i}.cs", $"/srv/repo/src/m{i / 100}/f{i}.cs"));

            Assert.AreEqual(20000, _set.Count);
            var elapsed = DateTime.UtcNow - started;
            Assert.IsTrue(elapsed < TimeSpan.FromSeconds(5),
                $"queueing 20000 items took {elapsed.TotalSeconds:F1}s; the linear scan it replaced took about 10");
        }
    }
}
