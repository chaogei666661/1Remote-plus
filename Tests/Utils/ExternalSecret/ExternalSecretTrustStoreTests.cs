using System;
using System.IO;
using _1RM.Utils.ExternalSecret;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.ExternalSecret
{
    /// <summary>
    /// The gate in front of <c>cmd://</c>. These are the tests that turn the blanket test-run approval back
    /// off, because the thing being checked is precisely that an unapproved command does not run — the whole
    /// point being that write access to a server list must not be code execution.
    /// </summary>
    [TestClass]
    public class ExternalSecretTrustStoreTests
    {
        private string _storePath = "";
        private string _marker = "";
        private int _prompts;

        /// <summary>A command that leaves a file behind, so "did it run" is observable rather than inferred.</summary>
        private string MarkerCommand => $"cmd://echo ran> \"{_marker}\" & echo secret";

        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
            ExternalSecretResolver.ClearCache();

            _storePath = Path.Combine(Path.GetTempPath(), $"1rm-trust-{Guid.NewGuid():N}.json");
            _marker = Path.Combine(Path.GetTempPath(), $"1rm-trust-marker-{Guid.NewGuid():N}.txt");
            _prompts = 0;

            ExternalSecretTrustStore.AutoApproveForTests = false;
            ExternalSecretTrustStore.StorePathOverride = _storePath;
            ExternalSecretTrustStore.ResetForTests();
            Deny();
        }

        [TestCleanup]
        public void Cleanup()
        {
            ExternalSecretTrustStore.ResetForTests();
            ExternalSecretTrustStore.StorePathOverride = null;
            ExternalSecretTrustStore.AutoApproveForTests = true;
            ExternalSecretTrustStore.Confirm = (title, message) => false;
            ExternalSecretResolver.ClearCache();

            if (File.Exists(_storePath)) File.Delete(_storePath);
            if (File.Exists(_marker)) File.Delete(_marker);
        }

        private void Deny() => ExternalSecretTrustStore.Confirm = (title, message) => { _prompts++; return false; };
        private void Allow() => ExternalSecretTrustStore.Confirm = (title, message) => { _prompts++; return true; };

        [TestMethod]
        public void AnUnapprovedCommandIsNotRunAtAll()
        {
            var secret = ExternalSecretResolver.Resolve(MarkerCommand);

            Assert.AreEqual("", secret);
            Assert.IsFalse(File.Exists(_marker), "the command must not have been executed");
        }

        [TestMethod]
        public void ADeclinedCommandIsOnlyAskedAboutOncePerRun()
        {
            // A server with a password and a key passphrase would otherwise produce a dialog per field.
            ExternalSecretResolver.Resolve(MarkerCommand);
            ExternalSecretResolver.Resolve(MarkerCommand);

            Assert.AreEqual(1, _prompts);
        }

        [TestMethod]
        public void ApprovingRunsTheCommandAndIsRememberedWithoutAskingAgain()
        {
            Allow();

            Assert.AreEqual("secret", ExternalSecretResolver.Resolve(MarkerCommand));
            Assert.IsTrue(File.Exists(_marker));

            ExternalSecretResolver.ClearCache();
            Assert.AreEqual("secret", ExternalSecretResolver.Resolve(MarkerCommand));
            Assert.AreEqual(1, _prompts, "the approval should have been remembered");
        }

        [TestMethod]
        public void AnApprovalSurvivesARestartOfTheStore()
        {
            Allow();
            ExternalSecretTrustStore.Approve("bw get password x");

            ExternalSecretTrustStore.ResetForTests();

            Assert.IsTrue(File.Exists(_storePath), "the approval should have been written to the locality file");
            Assert.IsTrue(ExternalSecretTrustStore.IsApproved("bw get password x"));
        }

        [TestMethod]
        public void AnApprovalStoreFromAnotherMachineApprovesNothingHere()
        {
            // Exactly the case a restored .1rbak or a synced locality folder produces: the entries are real
            // but they were not given on this machine, so they must not count.
            File.WriteAllText(_storePath, "{ \"someHashFromSomewhereElse\": \"bw get password x\" }");
            ExternalSecretTrustStore.ResetForTests();

            Assert.IsFalse(ExternalSecretTrustStore.IsApproved("bw get password x"));
        }

        [TestMethod]
        public void ApprovalIsPerExactCommandString()
        {
            ExternalSecretTrustStore.Approve("bw get password x");

            Assert.IsTrue(ExternalSecretTrustStore.IsApproved("bw get password x"));
            Assert.IsFalse(ExternalSecretTrustStore.IsApproved("bw get password x && calc.exe"));
        }

        [TestMethod]
        public void PressingTestApprovesTheCommandSoConnectingDoesNotAskAgain()
        {
            // The documented policy: the test button is the approval, because the user is looking at the
            // command they just typed when they press it.
            var (ok, message, _) = ExternalSecretResolver.Test("cmd://echo hunter2");
            Assert.IsTrue(ok, message);

            Assert.IsTrue(ExternalSecretTrustStore.IsApproved("echo hunter2"));
            Assert.AreEqual("hunter2", ExternalSecretResolver.Resolve("cmd://echo hunter2"));
            Assert.AreEqual(0, _prompts);
        }

        [TestMethod]
        public void AFailingTestDoesNotApproveAnything()
        {
            var (ok, _, _) = ExternalSecretResolver.Test("cmd://exit 3");

            Assert.IsFalse(ok);
            Assert.IsFalse(ExternalSecretTrustStore.IsApproved("exit 3"));
        }

        [TestMethod]
        public void APlainPasswordIsNeverSubjectToApproval()
        {
            Assert.AreEqual("hunter2", ExternalSecretResolver.Resolve("hunter2"));
            Assert.AreEqual(0, _prompts);
        }
    }
}
