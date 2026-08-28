using System;
using System.IO;
using System.Linq;
using _1RM.Utils.SessionRecording;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.SessionRecording
{
    /// <summary>
    /// Terminal recording had no retention at all: the folder grew for ever, and it holds whatever crossed
    /// the screen. These cases pin that both limits delete the right files and no others.
    /// </summary>
    [TestClass]
    public class SessionLogRetentionTests
    {
        private string _folder = "";

        [TestInitialize]
        public void Setup()
        {
            _folder = Path.Combine(Path.GetTempPath(), $"1rm-rec-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_folder);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
            }
            catch (Exception)
            {
                // a leftover temp folder is not worth failing a test over
            }
        }

        private string Write(string name, int bytes, DateTime lastWrite)
        {
            var path = Path.Combine(_folder, name);
            File.WriteAllBytes(path, new byte[bytes]);
            File.SetLastWriteTime(path, lastWrite);
            return path;
        }

        private string[] Remaining() =>
            Directory.GetFiles(_folder).Select(Path.GetFileName).OrderBy(x => x).ToArray()!;

        [TestMethod]
        public void TheAgeLimitDeletesOnlyWhatIsPastIt()
        {
            var now = new DateTime(2026, 6, 30, 12, 0, 0);
            Write("old.log", 10, now.AddDays(-40));
            Write("edge.log", 10, now.AddDays(-29));
            Write("new.log", 10, now.AddHours(-1));

            Assert.AreEqual(1, SessionLogRetention.Prune(_folder, maxAgeDays: 30, maxMegabytes: 0, now));
            CollectionAssert.AreEqual(new[] { "edge.log", "new.log" }, Remaining());
        }

        [TestMethod]
        public void TheSizeLimitDropsTheOldestFirst()
        {
            var now = new DateTime(2026, 6, 30, 12, 0, 0);
            var mb = 1024 * 1024;
            Write("a-oldest.log", mb, now.AddDays(-3));
            Write("b-middle.log", mb, now.AddDays(-2));
            Write("c-newest.log", mb, now.AddDays(-1));

            // Budget of 2 MB against 3 MB present: exactly one file has to go, and it is the oldest.
            Assert.AreEqual(1, SessionLogRetention.Prune(_folder, maxAgeDays: 0, maxMegabytes: 2, now));
            CollectionAssert.AreEqual(new[] { "b-middle.log", "c-newest.log" }, Remaining());
        }

        [TestMethod]
        public void BothLimitsApplyInOnePass()
        {
            var now = new DateTime(2026, 6, 30, 12, 0, 0);
            var mb = 1024 * 1024;
            Write("ancient.log", mb, now.AddDays(-90));
            Write("old.log", mb, now.AddDays(-5));
            Write("new.log", mb, now.AddDays(-1));

            // Age takes ancient.log; size then has 2 MB against a 1 MB budget and takes old.log.
            Assert.AreEqual(2, SessionLogRetention.Prune(_folder, maxAgeDays: 30, maxMegabytes: 1, now));
            CollectionAssert.AreEqual(new[] { "new.log" }, Remaining());
        }

        [TestMethod]
        public void BothLimitsOffMeansNothingIsTouched()
        {
            var now = new DateTime(2026, 6, 30, 12, 0, 0);
            Write("ancient.log", 10, now.AddDays(-900));

            Assert.AreEqual(0, SessionLogRetention.Prune(_folder, maxAgeDays: 0, maxMegabytes: 0, now));
            CollectionAssert.AreEqual(new[] { "ancient.log" }, Remaining());
        }

        [TestMethod]
        public void FilesThatAreNotRecordingsAreLeftAlone()
        {
            var now = new DateTime(2026, 6, 30, 12, 0, 0);
            Write("old.log", 10, now.AddDays(-90));
            Write("notes.txt", 10, now.AddDays(-90));
            Write("keep.db", 10, now.AddDays(-90));

            Assert.AreEqual(1, SessionLogRetention.Prune(_folder, maxAgeDays: 30, maxMegabytes: 0, now));
            CollectionAssert.AreEqual(new[] { "keep.db", "notes.txt" }, Remaining());
        }

        [TestMethod]
        public void AFolderThatDoesNotExistIsNotAnError()
        {
            var missing = Path.Combine(_folder, "nope");
            Assert.AreEqual(0, SessionLogRetention.Prune(missing, 30, 100, DateTime.Now));
            Assert.AreEqual(0, SessionLogRetention.TotalBytes(missing));
        }

        [TestMethod]
        public void AnEmptyFolderPathIsIgnoredRatherThanInterpreted()
        {
            // Guards against an unset setting being read as the current directory and pruning it.
            Assert.AreEqual(0, SessionLogRetention.Prune("", 1, 1, DateTime.Now));
            Assert.AreEqual(0, SessionLogRetention.Prune("   ", 1, 1, DateTime.Now));
        }

        [TestMethod]
        public void TotalBytesCountsOnlyRecordings()
        {
            var now = DateTime.Now;
            Write("a.log", 100, now);
            Write("b.log", 50, now);
            Write("c.txt", 999, now);

            Assert.AreEqual(150, SessionLogRetention.TotalBytes(_folder));
        }

        [TestMethod]
        public void ASubfolderIsNotDescendedInto()
        {
            var now = new DateTime(2026, 6, 30, 12, 0, 0);
            var sub = Path.Combine(_folder, "archive");
            Directory.CreateDirectory(sub);
            var kept = Path.Combine(sub, "old.log");
            File.WriteAllBytes(kept, new byte[10]);
            File.SetLastWriteTime(kept, now.AddDays(-900));

            Assert.AreEqual(0, SessionLogRetention.Prune(_folder, maxAgeDays: 30, maxMegabytes: 0, now));
            Assert.IsTrue(File.Exists(kept), "a folder the user made to keep things must not be swept");
        }
    }
}
