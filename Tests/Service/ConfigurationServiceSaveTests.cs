using System;
using System.IO;
using _1RM.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Service
{
    /// <summary>
    /// Save() guards itself with a CanSave flag that it cleared on entry and set again at the end. Every
    /// path that left in between - a directory it could not create, a throw out of the serializer or out of
    /// the additional-data-source write - left the flag false, and from then on Save() returned at its
    /// first line. The user kept changing settings and none of them were kept, with nothing said until the
    /// next launch.
    /// </summary>
    [TestClass]
    public class ConfigurationServiceSaveTests
    {
        private string _root = "";
        private AppPathHelper _originalPaths = AppPathHelper.Instance;

        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
            _originalPaths = AppPathHelper.Instance;
            _root = Path.Combine(Path.GetTempPath(), $"1rm-cfg-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);
            AppPathHelper.Instance = new AppPathHelper(_root, _root);
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

        private static ConfigurationService NewService() =>
            new ConfigurationService(new KeywordMatchService(), new Configuration());

        [TestMethod]
        public void AFailedWriteDoesNotStopEveryLaterSave()
        {
            var service = NewService();

            // Make the profile path unwritable by putting a directory where the file has to go. This is the
            // shape of the real failures: a locked file, a read-only folder, a full disk.
            var profilePath = AppPathHelper.Instance.ProfileJsonPath;
            if (File.Exists(profilePath)) File.Delete(profilePath);
            Directory.CreateDirectory(profilePath);

            service.General.ConfirmBeforeClosingSession = true;
            service.Save();
            Assert.IsTrue(service.CanSave, "a failed write must not disable saving for the rest of the session");

            // Clear the obstruction; the next save has to actually reach the disk.
            Directory.Delete(profilePath);
            service.General.ConfirmBeforeClosingSession = false;
            service.Save();

            Assert.IsTrue(File.Exists(profilePath), "the save after the obstruction was cleared did not write");
        }

        [TestMethod]
        public void AFailedWriteIsNotRememberedAsIfItHadSucceeded()
        {
            var service = NewService();
            var profilePath = AppPathHelper.Instance.ProfileJsonPath;

            // First save succeeds and establishes a baseline.
            service.General.CurrentLanguageCode = "en-us";
            service.Save();
            Assert.IsTrue(File.Exists(profilePath));

            // Now break the write, change something, and save. The change is lost - that is expected.
            var blocker = new FileStream(profilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            try
            {
                service.General.ConfirmBeforeClosingSession = true;
                service.Save();
            }
            finally
            {
                blocker.Dispose();
            }

            // The bug: the failed content was cached as "last saved", so this second save with the same
            // content took the nothing-changed path and the setting never reached the disk at all.
            service.Save();
            StringAssert.Contains(File.ReadAllText(profilePath), "\"ConfirmBeforeClosingSession\": true");
        }

        [TestMethod]
        public void AnUnchangedSaveStillDoesNotRewriteTheFile()
        {
            // The skip-when-unchanged optimisation has to survive the fix above.
            var service = NewService();
            service.General.CurrentLanguageCode = "en-us";
            service.Save();

            var profilePath = AppPathHelper.Instance.ProfileJsonPath;
            var firstWrite = File.GetLastWriteTimeUtc(profilePath);
            File.SetLastWriteTimeUtc(profilePath, firstWrite.AddDays(-1));
            var marker = File.GetLastWriteTimeUtc(profilePath);

            service.Save();

            Assert.AreEqual(marker, File.GetLastWriteTimeUtc(profilePath),
                "saving unchanged settings should not touch the file");
        }
    }
}
