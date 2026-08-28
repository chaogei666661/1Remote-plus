using System;
using System.Collections.Generic;
using System.Text;
using _1RM.Utils.Proxy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.Proxy
{
    /// <summary>
    /// Whether the bastion is allowed to be whoever it claims to be.
    ///
    /// SSH.NET trusts every host key unless a handler refuses, and until this gate existed no handler was
    /// subscribed on the jump-host connection — so the password or key passphrase for the bastion went to
    /// whatever answered on its address, and every session and standing forward routed through it inherited
    /// that unauthenticated transport. SFTP had been fixed for exactly this in an earlier round; the jump
    /// host had not.
    ///
    /// These cases drive the decision directly rather than through SSH.NET, because
    /// <c>HostKeyEventArgs</c> cannot be constructed outside the library. They are byte arrays and a
    /// delegate, so they give the same answer on Windows and on Linux.
    /// </summary>
    [TestClass]
    public class SshHostKeyGateTests
    {
        private SshHostKeyGate.VerifyDelegate _original = null!;

        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
            _original = SshHostKeyGate.Verify;
        }

        [TestCleanup]
        public void Restore()
        {
            SshHostKeyGate.Verify = _original;
        }

        private static byte[] AKey(string seed = "ssh-ed25519 AAAAC3Nza") => Encoding.UTF8.GetBytes(seed);

        /// <summary>
        /// The default has to be a refusal. If a wiring mistake left the gate unconnected, an "allow"
        /// default would restore the exact hole this class was written for, while still looking like a
        /// check — a failed login is the far cheaper failure.
        /// </summary>
        [TestMethod]
        public void AGateNobodyWiredRefusesRatherThanWavesThrough()
        {
            // the field is restored by TestCleanup; this asserts the shipped default, not a leftover
            SshHostKeyGate.Verify = _original;

            var trusted = SshHostKeyGate.IsTrusted("jump.example.com", 22, AKey());

            Assert.IsFalse(trusted);
        }

        [TestMethod]
        public void AVerifierThatAcceptsLetsTheConnectionThrough()
        {
            SshHostKeyGate.Verify = (_, _, _, _) => true;

            Assert.IsTrue(SshHostKeyGate.IsTrusted("jump.example.com", 22, AKey()));
        }

        [TestMethod]
        public void AVerifierThatDeclinesStopsTheConnection()
        {
            SshHostKeyGate.Verify = (_, _, _, _) => false;

            Assert.IsFalse(SshHostKeyGate.IsTrusted("jump.example.com", 22, AKey()));
        }

        /// <summary>
        /// The host, port and key are what the trust store keys on. Passing the wrong one would pin a
        /// fingerprint against a host nobody dialled, and the next connection would ask again.
        /// </summary>
        [TestMethod]
        public void TheVerifierIsAskedAboutTheHostAndKeyItWasGiven()
        {
            var seen = new List<string>();
            byte[]? seenKey = null;
            SshHostKeyGate.Verify = (host, port, key, detail) =>
            {
                seen.Add($"{host}:{port}");
                seen.Add(detail);
                seenKey = key;
                return true;
            };

            SshHostKeyGate.IsTrusted("bastion.corp", 2222, AKey("abc"), "SSH jump host \"corp\"");

            CollectionAssert.AreEqual(new[] { "bastion.corp:2222", "SSH jump host \"corp\"" }, seen);
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("abc"), seenKey);
        }

        /// <summary>
        /// A key we cannot see is a key we cannot pin. Remembering an empty fingerprint would make every
        /// later connection to that host compare equal to nothing and pass.
        /// </summary>
        [TestMethod]
        public void AHostThatPresentedNoKeyIsRefusedWithoutAsking()
        {
            var asked = false;
            SshHostKeyGate.Verify = (_, _, _, _) => { asked = true; return true; };

            Assert.IsFalse(SshHostKeyGate.IsTrusted("jump.example.com", 22, null));
            Assert.IsFalse(SshHostKeyGate.IsTrusted("jump.example.com", 22, Array.Empty<byte>()));
            Assert.IsFalse(asked, "an absent key is not a question for the user");
        }

        [TestMethod]
        public void AnEmptyAddressIsRefusedWithoutAsking()
        {
            var asked = false;
            SshHostKeyGate.Verify = (_, _, _, _) => { asked = true; return true; };

            Assert.IsFalse(SshHostKeyGate.IsTrusted("", 22, AKey()));
            Assert.IsFalse(SshHostKeyGate.IsTrusted("   ", 22, AKey()));
            Assert.IsFalse(asked);
        }

        /// <summary>
        /// This runs on SSH.NET's receive thread. The verifier reads a JSON file and shows a dialog, both
        /// of which can throw, and an exception escaping there would end the process rather than the login.
        /// </summary>
        [TestMethod]
        public void AVerifierThatThrowsBecomesARefusalAndNotACrash()
        {
            SshHostKeyGate.Verify = (_, _, _, _) => throw new InvalidOperationException("the trust store is unreadable");

            Assert.IsFalse(SshHostKeyGate.IsTrusted("jump.example.com", 22, AKey()));
        }

        /// <summary>
        /// The detail string is optional at the call site; a null must not become the exception that the
        /// case above turns into a refusal.
        /// </summary>
        [TestMethod]
        public void AMissingDetailIsPassedOnAsAnEmptyString()
        {
            string? seen = null;
            SshHostKeyGate.Verify = (_, _, _, detail) => { seen = detail; return true; };

            Assert.IsTrue(SshHostKeyGate.IsTrusted("jump.example.com", 22, AKey(), null!));
            Assert.AreEqual("", seen);
        }
    }
}
