using System;
using System.IO;
using System.Text;
using _1RM.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Service
{
    /// <summary>
    /// The trust-on-first-use store behind SFTP host keys and FTPS certificates. What matters is that the
    /// second connection to a known host is silent, and that a fingerprint which has changed is not.
    /// </summary>
    [TestClass]
    public class HostTrustServiceTests
    {
        private string _root = "";
        private AppPathHelper _originalPaths = AppPathHelper.Instance;
        private int _prompts;

        private const string FINGERPRINT_A = "SHA256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string FINGERPRINT_B = "SHA256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
            _originalPaths = AppPathHelper.Instance;
            _root = Path.Combine(Path.GetTempPath(), $"1rm-hosttrust-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);
            AppPathHelper.Instance = new AppPathHelper(_root, _root);
            _prompts = 0;
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

        private HostTrustService NewService(bool answer)
        {
            return new HostTrustService
            {
                Confirm = (title, message) => { _prompts++; return answer; }
            };
        }

        [TestMethod]
        public void AnUnknownHostIsAcceptedOnceTheUserSaysYes()
        {
            var service = NewService(answer: true);

            Assert.IsTrue(service.VerifyOrAsk("ssh", "10.0.0.1", 22, FINGERPRINT_A));
            Assert.AreEqual(1, _prompts);
        }

        [TestMethod]
        public void AnUnknownHostIsRefusedWhenTheUserSaysNo()
        {
            var service = NewService(answer: false);

            Assert.IsFalse(service.VerifyOrAsk("ssh", "10.0.0.1", 22, FINGERPRINT_A));
        }

        [TestMethod]
        public void AHostThatWasAcceptedIsNotAskedAboutAgain()
        {
            var service = NewService(answer: true);
            service.VerifyOrAsk("ssh", "10.0.0.1", 22, FINGERPRINT_A);

            Assert.IsTrue(service.VerifyOrAsk("ssh", "10.0.0.1", 22, FINGERPRINT_A));
            Assert.AreEqual(1, _prompts, "the second connection should have been silent");
        }

        [TestMethod]
        public void AFingerprintThatChangedIsPutBackToTheUser()
        {
            var service = NewService(answer: true);
            service.VerifyOrAsk("ssh", "10.0.0.1", 22, FINGERPRINT_A);
            _prompts = 0;

            // Same host, different key: either the server was rebuilt or something is sitting in the middle,
            // and only the user can tell which.
            var refusing = NewService(answer: false);
            Assert.IsFalse(refusing.VerifyOrAsk("ssh", "10.0.0.1", 22, FINGERPRINT_B));
            Assert.AreEqual(1, _prompts);
        }

        [TestMethod]
        public void AcceptingAChangedFingerprintReplacesTheOldOne()
        {
            var service = NewService(answer: true);
            service.VerifyOrAsk("ssh", "10.0.0.1", 22, FINGERPRINT_A);
            service.VerifyOrAsk("ssh", "10.0.0.1", 22, FINGERPRINT_B);
            _prompts = 0;

            Assert.IsTrue(service.VerifyOrAsk("ssh", "10.0.0.1", 22, FINGERPRINT_B));
            Assert.AreEqual(0, _prompts);
        }

        [TestMethod]
        public void TrustIsPerHostPortAndKind()
        {
            var service = NewService(answer: true);
            service.VerifyOrAsk("ssh", "10.0.0.1", 22, FINGERPRINT_A);
            _prompts = 0;

            service.VerifyOrAsk("ssh", "10.0.0.1", 2222, FINGERPRINT_A);
            service.VerifyOrAsk("ssh", "10.0.0.2", 22, FINGERPRINT_A);
            service.VerifyOrAsk("tls", "10.0.0.1", 22, FINGERPRINT_A);

            Assert.AreEqual(3, _prompts, "an SSH host key and a TLS certificate are not the same identity");
        }

        [TestMethod]
        public void AnAcceptedHostSurvivesARestart()
        {
            NewService(answer: true).VerifyOrAsk("tls", "ftp.example.com", 990, FINGERPRINT_A);

            Assert.IsTrue(File.Exists(AppPathHelper.Instance.HostTrustJsonPath), "nothing was written to .locality");

            var afterRestart = NewService(answer: false);
            Assert.IsTrue(afterRestart.VerifyOrAsk("tls", "ftp.example.com", 990, FINGERPRINT_A));
        }

        [TestMethod]
        public void AnUnreadableStoreDoesNotTakeTheConnectionDownWithIt()
        {
            var path = AppPathHelper.Instance.HostTrustJsonPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "this is not json", Encoding.UTF8);

            var service = NewService(answer: true);

            Assert.IsTrue(service.VerifyOrAsk("ssh", "10.0.0.1", 22, FINGERPRINT_A), "it should fall back to asking");
        }

        [TestMethod]
        public void TheFingerprintIsStableAndDependsOnEveryByte()
        {
            var key = new byte[] { 1, 2, 3, 4 };

            var fingerprint = HostTrustService.Fingerprint(key);

            Assert.AreEqual(fingerprint, HostTrustService.Fingerprint(new byte[] { 1, 2, 3, 4 }));
            Assert.AreNotEqual(fingerprint, HostTrustService.Fingerprint(new byte[] { 1, 2, 3, 5 }));
            StringAssert.StartsWith(fingerprint, "SHA256:");
            Assert.IsFalse(fingerprint.EndsWith("="), "the base64 padding is trimmed for display");
        }
    }
}
