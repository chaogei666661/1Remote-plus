using System;
using System.IO;
using _1RM.Utils.FileTransmit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.FileTransmit
{
    /// <summary>
    /// A recursive SFTP or FTP download builds every local path out of names the server chose. Before the
    /// guard, those went straight into Path.Combine, which drops the destination folder entirely when the
    /// second argument is rooted and never resolves "..". These cases are the listings a hostile server would
    /// send.
    ///
    /// The escape cases run against a real directory under the temp folder, so a path that is claimed to be
    /// contained is one the file system agrees with.
    /// </summary>
    [TestClass]
    public class DownloadPathGuardTests
    {
        private string _downloads = "";

        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
            _downloads = Path.Combine(Path.GetTempPath(), "1remote-guard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_downloads, "wanted"));
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_downloads))
                    Directory.Delete(_downloads, true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test run over.
            }
        }

        private void AssertRefused(string remoteName)
        {
            var thrown = Assert.ThrowsException<UnsafeRemoteNameException>(
                () => DownloadPathGuard.Resolve(_downloads, remoteName),
                $"'{remoteName}' was accepted");
            Assert.AreEqual(remoteName.Length == 0 ? "(empty)" : remoteName, thrown.RemoteName,
                "the message has to be able to name what was refused");
        }

        [TestMethod]
        public void AnOrdinaryNameLandsInTheChosenFolder()
        {
            var resolved = DownloadPathGuard.Resolve(_downloads, "report.pdf");

            Assert.AreEqual(Path.Combine(_downloads, "report.pdf"), resolved);
        }

        [TestMethod]
        public void ARelativePathKeepsItsFolderStructure()
        {
            var resolved = DownloadPathGuard.Resolve(_downloads, "wanted/inner/report.pdf");

            Assert.AreEqual(Path.Combine(_downloads, "wanted", "inner", "report.pdf"), resolved);
        }

        [TestMethod]
        public void LeadingTrailingAndDoubledSeparatorsAreIgnoredRatherThanRefused()
        {
            // A server that answers "/wanted//report.pdf" for an entry of the directory being copied is
            // sloppy, not hostile, and the path it describes is still inside the destination.
            var resolved = DownloadPathGuard.Resolve(_downloads, "/wanted//report.pdf");

            Assert.AreEqual(Path.Combine(_downloads, "wanted", "report.pdf"), resolved);
        }

        [TestMethod]
        public void ACurrentDirectorySegmentIsDroppedRatherThanRefused()
        {
            var resolved = DownloadPathGuard.Resolve(_downloads, "./wanted/./report.pdf");

            Assert.AreEqual(Path.Combine(_downloads, "wanted", "report.pdf"), resolved);
        }

        [TestMethod]
        public void AParentSegmentIsRefused()
        {
            AssertRefused("../evil.exe");
            AssertRefused("wanted/../../evil.exe");
        }

        /// <summary>
        /// The case a POSIX server is uniquely placed to send: a backslash is an ordinary character in a
        /// file name there, so the entry is legal on the wire and a directory separator by the time Windows
        /// sees it. Refused on Linux too, because the code that will run it is the same code.
        /// </summary>
        [TestMethod]
        public void ABackslashParentSegmentIsRefusedOnEveryPlatform()
        {
            AssertRefused(@"..\..\evil.exe");
            AssertRefused(@"wanted\..\..\evil.exe");
        }

        [TestMethod]
        public void ARunOfDotsIsRefusedBecauseWindowsNormalisesItToOne()
        {
            AssertRefused("...");
            AssertRefused("wanted/..../evil.exe");
        }

        /// <summary>
        /// A leading separator cannot be treated as an attack, because the caller produces one legitimately:
        /// it strips the parent prefix off the entry's full path and what is left starts with "/". So an
        /// absolute POSIX path is re-rooted under the destination instead of being honoured — where
        /// Path.Combine would have returned "/etc/cron.d/x" and discarded the destination entirely.
        /// </summary>
        [TestMethod]
        public void AnAbsolutePosixPathIsRerootedUnderTheDestination()
        {
            var resolved = DownloadPathGuard.Resolve(_downloads, "/etc/cron.d/x");

            Assert.AreEqual(Path.Combine(_downloads, "etc", "cron.d", "x"), resolved);
            Assert.IsTrue(DownloadPathGuard.IsContained(_downloads, resolved));
        }

        [TestMethod]
        public void AWindowsAbsolutePathIsRefused()
        {
            AssertRefused(@"C:\Windows\System32\evil.dll");
            AssertRefused("C:/Windows/System32/evil.dll");
        }

        /// <summary>
        /// "C:x" resolves against the working directory of drive C and is rooted as far as Path.Combine is
        /// concerned, so it discards the destination just as a full drive path does.
        /// </summary>
        [TestMethod]
        public void ADriveRelativePathIsRefused()
        {
            AssertRefused("C:evil.exe");
        }

        /// <summary>
        /// Same reasoning as the POSIX case: the leading separators are dropped and the rest becomes a
        /// subfolder, so the machine named in the path is never contacted.
        /// </summary>
        [TestMethod]
        public void AUncPathIsRerootedUnderTheDestination()
        {
            var resolved = DownloadPathGuard.Resolve(_downloads, @"\\attacker\share\evil.exe");

            Assert.AreEqual(Path.Combine(_downloads, "attacker", "share", "evil.exe"), resolved);
            Assert.IsTrue(DownloadPathGuard.IsContained(_downloads, resolved));
        }

        /// <summary>
        /// The payload would sit in an alternate data stream of a file whose name looks innocent, and would
        /// not appear in the folder at all.
        /// </summary>
        [TestMethod]
        public void AnAlternateDataStreamNameIsRefused()
        {
            AssertRefused("notes.txt:evil.exe");
        }

        [TestMethod]
        public void AnEmptyNameIsRefused()
        {
            AssertRefused("");
            AssertRefused("/");
            AssertRefused("///");
        }

        /// <summary>
        /// Characters Win32 will not accept in a name are left to the file system, which reports them more
        /// accurately: they cannot redirect a write, so refusing the whole transfer over one would be a false
        /// alarm that costs the user the other files.
        /// </summary>
        [TestMethod]
        public void AnIllegalButHarmlessCharacterIsNotTreatedAsAnAttack()
        {
            var resolved = DownloadPathGuard.Resolve(_downloads, "why?.txt");

            Assert.AreEqual(Path.Combine(_downloads, "why?.txt"), resolved);
        }

        [TestMethod]
        public void TryResolveReportsTheSameDecisionsWithoutThrowing()
        {
            Assert.IsTrue(DownloadPathGuard.TryResolve(_downloads, "report.pdf", out var good));
            Assert.AreEqual(Path.Combine(_downloads, "report.pdf"), good);

            Assert.IsFalse(DownloadPathGuard.TryResolve(_downloads, "../evil.exe", out var bad));
            Assert.AreEqual("", bad);
        }

        [TestMethod]
        public void IsSafeSegmentAnswersForASingleListingEntry()
        {
            Assert.IsTrue(DownloadPathGuard.IsSafeSegment("report.pdf"));
            Assert.IsTrue(DownloadPathGuard.IsSafeSegment(".hidden"));
            Assert.IsTrue(DownloadPathGuard.IsSafeSegment("..hidden"));

            Assert.IsFalse(DownloadPathGuard.IsSafeSegment(null));
            Assert.IsFalse(DownloadPathGuard.IsSafeSegment(""));
            Assert.IsFalse(DownloadPathGuard.IsSafeSegment("."));
            Assert.IsFalse(DownloadPathGuard.IsSafeSegment(".."));
            Assert.IsFalse(DownloadPathGuard.IsSafeSegment("a/b"));
            Assert.IsFalse(DownloadPathGuard.IsSafeSegment(@"a\b"));
        }

        [TestMethod]
        public void IsContainedAcceptsTheRootItselfAndItsChildren()
        {
            Assert.IsTrue(DownloadPathGuard.IsContained(_downloads, _downloads));
            Assert.IsTrue(DownloadPathGuard.IsContained(_downloads, Path.Combine(_downloads, "wanted")));
            Assert.IsTrue(DownloadPathGuard.IsContained(_downloads, Path.Combine(_downloads, "wanted", "deep", "f.txt")));
        }

        /// <summary>
        /// A prefix match without a separator would call the sibling folder "1remote-guard-abc-elsewhere" a
        /// child of "1remote-guard-abc".
        /// </summary>
        [TestMethod]
        public void IsContainedRejectsASiblingWhoseNameStartsWithTheRoot()
        {
            Assert.IsFalse(DownloadPathGuard.IsContained(_downloads, _downloads + "-elsewhere"));
            Assert.IsFalse(DownloadPathGuard.IsContained(_downloads, _downloads + "-elsewhere/f.txt"));
        }

        [TestMethod]
        public void IsContainedResolvesParentSegmentsBeforeDeciding()
        {
            Assert.IsFalse(DownloadPathGuard.IsContained(_downloads, Path.Combine(_downloads, "..", "evil.exe")));
            Assert.IsTrue(DownloadPathGuard.IsContained(_downloads, Path.Combine(_downloads, "wanted", "..", "f.txt")));
        }

        [TestMethod]
        public void AResolvedPathCanActuallyBeWritten()
        {
            var resolved = DownloadPathGuard.Resolve(_downloads, "wanted/inner/report.pdf");

            Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
            File.WriteAllText(resolved, "x");

            Assert.IsTrue(File.Exists(Path.Combine(_downloads, "wanted", "inner", "report.pdf")),
                "the guard produced a path the file system does not agree with");
        }

        [TestMethod]
        public void AMissingDestinationIsAProgrammingErrorNotAnAttack()
        {
            Assert.ThrowsException<ArgumentException>(() => DownloadPathGuard.Resolve("", "report.pdf"));
        }
    }
}
