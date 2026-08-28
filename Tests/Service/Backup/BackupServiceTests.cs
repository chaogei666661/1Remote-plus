using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using _1RM.Service;
using _1RM.Service.Backup;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Service.Backup
{
    /// <summary>
    /// A .1rbak is untrusted input: it arrives by email, from a shared drive, or from whoever asked the user
    /// to "restore this". These tests are mostly about what restore refuses to do with one.
    /// </summary>
    [TestClass]
    public class BackupServiceTests
    {
        private const string MANIFEST_ENTRY = "1remote-backup.txt";

        private string _root = "";
        private string _archive = "";
        private AppPathHelper _originalPaths = AppPathHelper.Instance;

        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
            _originalPaths = AppPathHelper.Instance;
            _root = Path.Combine(Path.GetTempPath(), $"1rm-backup-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);
            AppPathHelper.Instance = new AppPathHelper(_root, _root);
            _archive = Path.Combine(_root, "backup" + BackupService.FILE_EXTENSION);
        }

        [TestCleanup]
        public void Cleanup()
        {
            AppPathHelper.Instance = _originalPaths;
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
            }
            catch (Exception)
            {
                // a leftover temp folder is not worth failing a test over
            }
        }

        private static void Write(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        /// <summary>Builds an archive entry by entry, which is how a hostile one would be built too.</summary>
        private void WriteArchive(params (string entryName, string content)[] entries)
        {
            using var file = new FileStream(_archive, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);
            foreach (var (entryName, content) in entries)
            {
                using var stream = archive.CreateEntry(entryName).Open();
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.Write(content);
            }
        }

        [TestMethod]
        public void ARoundTripBringsTheConfigurationBack()
        {
            Write(AppPathHelper.Instance.ProfileJsonPath, "{ \"profile\": true }");
            Write(Path.Combine(AppPathHelper.Instance.LocalityDirPath, "known_hosts.json"), "{}");

            Assert.AreEqual(2, BackupService.Create(_archive));
            Assert.IsTrue(BackupService.IsBackup(_archive));

            File.Delete(AppPathHelper.Instance.ProfileJsonPath);
            File.Delete(Path.Combine(AppPathHelper.Instance.LocalityDirPath, "known_hosts.json"));

            BackupService.Restore(_archive);

            Assert.AreEqual("{ \"profile\": true }", File.ReadAllText(AppPathHelper.Instance.ProfileJsonPath));
            Assert.IsTrue(File.Exists(Path.Combine(AppPathHelper.Instance.LocalityDirPath, "known_hosts.json")));
        }

        [TestMethod]
        public void AnEntryThatClimbsOutOfItsFolderIsNotWritten()
        {
            var name = $"pwned-{Guid.NewGuid():N}.txt";
            WriteArchive(
                (MANIFEST_ENTRY, "1Remote backup"),
                ($"locality/../../{name}", "owned"),
                ("locality/keep.json", "{}"));

            BackupService.Restore(_archive);

            Assert.IsFalse(File.Exists(Path.Combine(_root, name)), "the archive walked out of the locality folder");
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, name)), "and out of the app folder as well");
            Assert.IsTrue(File.Exists(Path.Combine(AppPathHelper.Instance.LocalityDirPath, "keep.json")),
                "the harmless entry beside it should still have been restored");
        }

        [TestMethod]
        public void AnEntryUsingBackslashesCannotClimbOutEither()
        {
            // Zip entry names are supposed to use forward slashes, so a backslash is a hint that someone is
            // trying to get past a check that only looks at one separator.
            var name = $"pwned-{Guid.NewGuid():N}.txt";
            WriteArchive(
                (MANIFEST_ENTRY, "1Remote backup"),
                ($@"locality\..\..\{name}", "owned"));

            BackupService.Restore(_archive);

            Assert.IsFalse(File.Exists(Path.Combine(_root, name)));
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, name)));
        }

        [TestMethod]
        public void AnAbsolutePathIsNotWrittenEither()
        {
            var absolute = Path.Combine(_root, "absolute.txt");
            WriteArchive(
                (MANIFEST_ENTRY, "1Remote backup"),
                (absolute.Replace('\\', '/').TrimStart('/'), "owned"));

            BackupService.Restore(_archive);

            Assert.IsFalse(File.Exists(absolute));
        }

        [TestMethod]
        public void AnEntryForSomethingWeDoNotOwnIsSkipped()
        {
            WriteArchive(
                (MANIFEST_ENTRY, "1Remote backup"),
                ("autorun.inf", "[autorun]"));

            BackupService.Restore(_archive);

            Assert.IsFalse(File.Exists(Path.Combine(_root, "autorun.inf")));
        }

        [TestMethod]
        public void AZipThatIsNotOneOfOursIsRefused()
        {
            WriteArchive(("profile.json", "{}"));

            Assert.IsFalse(BackupService.IsBackup(_archive));
            Assert.ThrowsException<InvalidDataException>(() => BackupService.Restore(_archive));
            Assert.IsFalse(File.Exists(AppPathHelper.Instance.ProfileJsonPath));
        }

        [TestMethod]
        public void AFileThatIsNotAZipIsNotABackup()
        {
            File.WriteAllText(_archive, "this is a text file with a .1rbak name");

            Assert.IsFalse(BackupService.IsBackup(_archive));
        }

        /// <summary>
        /// The suggested name used to be an interpolated <c>DateTime.Now</c>, which formats the year in the
        /// current culture's calendar. On a Thai-locale desktop the backup came out named 2569…, on a Hijri
        /// one 1448…, so a folder of archives from a mixed fleet neither sorted nor matched.
        /// </summary>
        [TestMethod]
        public void TheSuggestedNameUsesTheGregorianYearWhateverTheDesktopsCalendarIs()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("th-TH");
                var name = BackupService.SuggestedFileName();

                StringAssert.Contains(name, DateTime.Now.Year.ToString(CultureInfo.InvariantCulture));
                Assert.IsFalse(name.Contains((DateTime.Now.Year + 543).ToString(CultureInfo.InvariantCulture)),
                    "the Buddhist year does not belong in a file name");
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [TestMethod]
        public void TheSuggestedNameIsAnArchiveAndCarriesASortableStamp()
        {
            var name = BackupService.SuggestedFileName();

            StringAssert.EndsWith(name, BackupService.FILE_EXTENSION);
            StringAssert.Matches(name, new Regex(@"-\d{8}-\d{6}\" + BackupService.FILE_EXTENSION + "$"),
                "the stamp has to be 24-hour and sort as text, or two backups a day apart read out of order");
        }

        /// <summary>
        /// The manifest is what somebody comparing two archives reads. It was written in local time and in
        /// the ambient calendar, so two backups taken a minute apart in different time zones looked hours
        /// apart, and one from a Thai desktop was dated in a different millennium.
        /// </summary>
        [TestMethod]
        public void TheManifestRecordsWhenItWasTakenInUtcAndInTheGregorianCalendar()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("th-TH");
                Write(AppPathHelper.Instance.ProfileJsonPath, "{}");
                BackupService.Create(_archive);

                using var file = new FileStream(_archive, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var archive = new ZipArchive(file, ZipArchiveMode.Read);
                using var reader = new StreamReader(archive.GetEntry(MANIFEST_ENTRY)!.Open(), Encoding.UTF8);
                var manifest = reader.ReadToEnd();

                var created = Regex.Match(manifest, @"created=(?<stamp>[^\r\n]+)");
                Assert.IsTrue(created.Success, "the manifest has to say when the archive was taken");
                Assert.IsTrue(DateTime.TryParseExact(created.Groups["stamp"].Value, "yyyy-MM-dd HH:mm:ss'Z'",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                    $"'{created.Groups["stamp"].Value}' is not a UTC stamp in the invariant calendar");
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        /// <summary>
        /// A round trip under the same locale, because the calendar was not the only thing a Thai desktop
        /// broke here. Every log call goes through <c>SimpleLogHelper</c>, which cuts the directory off the
        /// calling frame's source path with the culture-sensitive <c>LastIndexOf("\\")</c>; ICU's Thai
        /// collation treats a backslash as ignorable and answers the length of the string instead of the
        /// position of the last separator, so the slice throws. On Windows, where source paths are full of
        /// backslashes, that turned a backup that had already been written to disk into an exception out of
        /// <see cref="BackupService.Create"/>. On Linux the frame's path has no backslash and it never
        /// fired, which is how it reached CI unnoticed.
        /// </summary>
        [TestMethod]
        public void ABackupIsStillTakenAndPutBackOnADesktopWhoseLocaleBreaksTheLogger()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("th-TH");
                Write(AppPathHelper.Instance.ProfileJsonPath, "{ \"profile\": true }");

                Assert.AreEqual(1, BackupService.Create(_archive));
                Assert.IsTrue(BackupService.IsBackup(_archive));

                File.Delete(AppPathHelper.Instance.ProfileJsonPath);
                BackupService.Restore(_archive);

                Assert.AreEqual("{ \"profile\": true }", File.ReadAllText(AppPathHelper.Instance.ProfileJsonPath));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        /// <summary>
        /// The same locale, over an archive with an entry that does not belong to us: <c>Restore</c> logs a
        /// warning for it and carries on, and that warning is on the same path through the logger as the
        /// one above.
        /// </summary>
        [TestMethod]
        public void AnUnexpectedEntryIsStillOnlySkippedWhenTheLoggerCannotFormatTheWarning()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("th-TH");
                WriteArchive(
                    (MANIFEST_ENTRY, "1Remote backup"),
                    ("autorun.inf", "[autorun]"),
                    ("locality/keep.json", "{}"));

                BackupService.Restore(_archive);

                Assert.IsFalse(File.Exists(Path.Combine(_root, "autorun.inf")));
                Assert.IsTrue(File.Exists(Path.Combine(AppPathHelper.Instance.LocalityDirPath, "keep.json")),
                    "the entry after the skipped one still has to be restored");
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }
}
