using System;
using System.Collections.Generic;
using System.Linq;
using _1RM.Utils.FileTransmit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.FileTransmit
{
    /// <summary>
    /// The transfer pane's status line is one 30-pixel row. Everything past its first line is invisible,
    /// so a notice that pastes a whole list into it hides the only part that matters — how many.
    ///
    /// These are string cases and give the same answer on Windows and on Linux.
    /// </summary>
    [TestClass]
    public class TransferNoticeTextTests
    {
        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
        }

        private static string More(int n) => $"and {n} more";

        [TestMethod]
        public void NothingToSayIsSaidAsNothing()
        {
            Assert.AreEqual("", TransferNoticeText.Summarise(null));
            Assert.AreEqual("", TransferNoticeText.Summarise(new string[0]));
            Assert.AreEqual("", TransferNoticeText.Summarise(new[] { "", "   " }));
        }

        [TestMethod]
        public void AListShorterThanTheLimitIsSpelledOutWhole()
        {
            Assert.AreEqual("proj/a, proj/b",
                TransferNoticeText.Summarise(new[] { "proj/a", "proj/b" }, 3, More));
        }

        [TestMethod]
        public void AListExactlyAtTheLimitDoesNotClaimThereAreMore()
        {
            Assert.AreEqual("a, b, c", TransferNoticeText.Summarise(new[] { "a", "b", "c" }, 3, More));
        }

        [TestMethod]
        public void TheOnesPastTheLimitAreCountedRatherThanListed()
        {
            Assert.AreEqual("a, b, c and 2 more",
                TransferNoticeText.Summarise(new[] { "a", "b", "c", "d", "e" }, 3, More));
        }

        /// <summary>
        /// The case this class exists for: a drive-root upload leaves hundreds of folders unread, and the
        /// old <c>string.Join</c> put every one of them into a control that shows one line.
        /// </summary>
        [TestMethod]
        public void AHugeListStillProducesOneShortLine()
        {
            var many = Enumerable.Range(0, 800).Select(i => $"drive/folder-{i}").ToList();

            var text = TransferNoticeText.Summarise(many, TransferNoticeText.DefaultLimit, More);

            Assert.IsTrue(text.Length < 120, $"a status line of {text.Length} characters is not a status line");
            StringAssert.EndsWith(text, "and 797 more");
        }

        [TestMethod]
        public void BlankEntriesDoNotBecomeStrayCommas()
        {
            Assert.AreEqual("a, b", TransferNoticeText.Summarise(new[] { "a", "", "  ", "b" }, 3, More));
        }

        [TestMethod]
        public void BlankEntriesAreNotCountedAsOmittedEither()
        {
            // Two real entries and a limit of two: nothing was left out, so nothing may claim there was.
            Assert.AreEqual("a, b", TransferNoticeText.Summarise(new[] { "a", "", "b" }, 2, More));
        }

        /// <summary>
        /// A path is cut at the front. The folder that was skipped is the tail, and the head is the folder
        /// the user chose and therefore already knows.
        /// </summary>
        [TestMethod]
        public void AVeryLongPathKeepsItsTailAndLosesItsHead()
        {
            var deep = "top/" + string.Join("/", Enumerable.Repeat("intermediate", 12)) + "/the-one-that-failed";

            var text = TransferNoticeText.Summarise(new[] { deep }, 3, More);

            Assert.IsTrue(text.Length <= 64, $"one entry took {text.Length} characters");
            StringAssert.StartsWith(text, "...");
            StringAssert.EndsWith(text, "the-one-that-failed");
        }

        [TestMethod]
        public void APathThatFitsIsNotTouched()
        {
            const string path = "proj/src/deep/note.txt";
            Assert.AreEqual(path, TransferNoticeText.Summarise(new[] { path }, 3, More));
        }

        [TestMethod]
        public void ALimitBelowOneStillNamesOne()
        {
            // A message that only counts and never names is not actionable, so zero is read as one.
            Assert.AreEqual("a and 2 more", TransferNoticeText.Summarise(new[] { "a", "b", "c" }, 0, More));
        }

        [TestMethod]
        public void WithoutATranslatorTheCountIsStillThere()
        {
            Assert.AreEqual("a, b, c (+1)", TransferNoticeText.Summarise(new[] { "a", "b", "c", "d" }, 3));
        }

        /// <summary>
        /// The message templates lead with the number, and the line lists a subset of the same collection.
        /// If the two disagreed the notice would read "1 folder(s): a, b".
        /// </summary>
        [TestMethod]
        public void TheCountAgreesWithWhatTheLineIsBuiltFrom()
        {
            var paths = new List<string> { "a", "", "b", "   ", "c", "d" };

            Assert.AreEqual(4, TransferNoticeText.Count(paths));
            Assert.AreEqual("a, b, c and 1 more", TransferNoticeText.Summarise(paths, 3, More));
            Assert.AreEqual(0, TransferNoticeText.Count(null));
        }

        [TestMethod]
        public void AnEmptyTranslationDoesNotLeaveATrailingSpace()
        {
            Assert.AreEqual("a, b", TransferNoticeText.Summarise(new[] { "a", "b", "c" }, 2, _ => ""));
        }
    }
}
