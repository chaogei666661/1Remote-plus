using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using _1RM.Service;
using _1RM.Service.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Service.Diagnostics
{
    [TestClass]
    public class DiagnosticsBundleTests
    {
        private string _root = "";
        private AppPathHelper _originalPaths = AppPathHelper.Instance;

        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
            _originalPaths = AppPathHelper.Instance;
            _root = Path.Combine(Path.GetTempPath(), $"1rm-diag-{Guid.NewGuid():N}");
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

        private static void Write(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        private static string Read(string zipPath, string entryName)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.GetEntry(entryName);
            Assert.IsNotNull(entry, $"{entryName} is missing from the bundle");
            using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static string[] Entries(string zipPath)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            return archive.Entries.Select(x => x.FullName).OrderBy(x => x).ToArray();
        }

        [TestMethod]
        public void AnEmptyInstallStillProducesAReadableBundle()
        {
            var zip = Path.Combine(_root, "out", "diag.zip");
            Assert.AreEqual(2, DiagnosticsBundle.Create(zip));

            CollectionAssert.AreEquivalent(new[] { "README.txt", "environment.txt" }, Entries(zip));
            StringAssert.Contains(Read(zip, "environment.txt"), "app.version");
        }

        [TestMethod]
        public void TheProfileIsIncludedWithItsSecretsStripped()
        {
            Write(AppPathHelper.Instance.ProfileJsonPath,
                "{\"WebDav\": {\"Url\": \"https://cloud.example.com/1rm\", \"Password\": \"hunter2\"}}");

            var zip = Path.Combine(_root, "diag.zip");
            DiagnosticsBundle.Create(zip);

            var profile = Read(zip, "profile.redacted.json");
            Assert.IsFalse(profile.Contains("hunter2"));
            StringAssert.Contains(profile, "cloud.example.com");
        }

        [TestMethod]
        public void RunnerDefinitionsAreIncludedWithoutTheirPasswordArguments()
        {
            Write(Path.Combine(AppPathHelper.Instance.ProtocolRunnerDirPath, "SSH.json"),
                "{\"Name\": \"PuTTY\", \"Arguments\": \"-ssh %1RM_HOSTNAME% -pw hunter2\"}");

            var zip = Path.Combine(_root, "diag.zip");
            DiagnosticsBundle.Create(zip);

            var runner = Read(zip, "protocols/SSH.json");
            Assert.IsFalse(runner.Contains("hunter2"));
            StringAssert.Contains(runner, "%1RM_HOSTNAME%");
        }

        [TestMethod]
        public void TheLogIsIncludedAndScrubbed()
        {
            Write(AppPathHelper.Instance.LogFilePath, "connecting\npassword=hunter2\ndone");

            var zip = Path.Combine(_root, "diag.zip");
            DiagnosticsBundle.Create(zip);

            var log = Read(zip, "app.log.md");
            Assert.IsFalse(log.Contains("hunter2"));
            StringAssert.Contains(log, "connecting");
        }

        [TestMethod]
        public void TheDatabaseAndTheTrustStoresAreNotInTheBundle()
        {
            Write(AppPathHelper.Instance.SqliteDbDefaultPath, "not really sqlite");
            Write(AppPathHelper.Instance.HostTrustJsonPath, "{}");
            Write(AppPathHelper.Instance.ExternalSecretTrustJsonPath, "{}");

            var zip = Path.Combine(_root, "diag.zip");
            DiagnosticsBundle.Create(zip);

            foreach (var entry in Entries(zip))
            {
                Assert.IsFalse(entry.EndsWith(".db", StringComparison.OrdinalIgnoreCase), entry);
                Assert.IsFalse(entry.Contains("known_hosts"), entry);
                Assert.IsFalse(entry.Contains("known_commands"), entry);
            }
        }

        [TestMethod]
        public void TheManifestSaysWhatWasStrippedAndWarnsThatItIsNotAProof()
        {
            var manifest = DiagnosticsBundle.BuildManifest();
            StringAssert.Contains(manifest, "password");
            StringAssert.Contains(manifest, "Not included");
            StringAssert.Contains(manifest, "not a proof");
        }

        [TestMethod]
        public void TheEnvironmentReportNamesNeitherTheUserNorTheMachine()
        {
            // The bundle is meant to be forwarded, so it should not carry the reporter's identity.
            var report = DiagnosticsBundle.BuildEnvironmentReport();
            Assert.IsFalse(report.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(report.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void WritingOverAnExistingBundleReplacesIt()
        {
            var zip = Path.Combine(_root, "diag.zip");
            File.WriteAllText(zip, "stale");
            DiagnosticsBundle.Create(zip);
            CollectionAssert.Contains(Entries(zip), "README.txt");
        }

        [TestMethod]
        public void TheSuggestedNameIsAZipAndCarriesAStamp()
        {
            var name = DiagnosticsBundle.SuggestedFileName();
            StringAssert.EndsWith(name, ".zip");
            StringAssert.Contains(name, "diagnostics");
        }
    }
}
