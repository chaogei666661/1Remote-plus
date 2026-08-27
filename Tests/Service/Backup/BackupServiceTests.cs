using System;
using System.IO;
using System.IO.Compression;
using System.Text;
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
    }
}
