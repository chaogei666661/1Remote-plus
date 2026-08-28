using System;
using System.Collections.Generic;
using System.Linq;
using _1RM.Utils.Proxy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Renci.SshNet;

namespace Tests.Utils.Proxy
{
    /// <summary>
    /// What SSH.NET is willing to negotiate on our behalf.
    ///
    /// The app pinned SSH.NET 2023.0.0 for three years. That release offered arcfour, blowfish, twofish,
    /// cast128, hmac-md5, hmac-ripemd160 and the truncated -96 MACs, advertised <c>ssh-dss</c>, and had no
    /// encrypt-then-MAC algorithm at all — which also meant no OpenSSH strict key exchange, so every SFTP
    /// session and every jump-host tunnel was negotiable down to a Terrapin-vulnerable transcript
    /// (CVE-2023-48795). None of that was visible in this repository, because the algorithm list lives in
    /// the library.
    ///
    /// These cases put the list under test, so that a future downgrade of the package reference is a red
    /// build rather than a silent loss of transport security. They ask no network and no window: the
    /// dictionaries are populated by the <see cref="ConnectionInfo"/> constructor.
    /// </summary>
    [TestClass]
    public class SshAlgorithmPolicyTests
    {
        [TestInitialize]
        public void Setup() => TestInit.Init();

        private static ConnectionInfo BuildJumpHostConnection() => SshConnectionFactory.Build(new ProxyConfig
        {
            Name = "jump",
            Type = EProxyType.SshJump,
            Address = "jump.example.com",
            Port = 22,
            UserName = "ops",
            Password = "secret",
        });

        /// <summary>
        /// Ciphers OpenSSH turned off in the server in 2014 and in the client in 2016. SSH.NET carried
        /// hand-written implementations of all of them until 2024.2.0.
        /// </summary>
        private static readonly string[] RetiredCiphers =
        {
            "arcfour", "arcfour128", "arcfour256",
            "blowfish-cbc",
            "cast128-cbc",
            "twofish-cbc", "twofish128-cbc", "twofish192-cbc", "twofish256-cbc",
        };

        private static readonly string[] RetiredMacs =
        {
            "hmac-md5", "hmac-md5-96",
            "hmac-sha1-96", "hmac-sha2-256-96", "hmac-sha2-512-96",
            "hmac-ripemd160", "hmac-ripemd160@openssh.com",
        };

        [TestMethod]
        public void TheRetiredCiphersAreNoLongerOffered()
        {
            var offered = BuildJumpHostConnection().Encryptions.Keys.ToList();

            foreach (var cipher in RetiredCiphers)
                CollectionAssert.DoesNotContain(offered, cipher, $"{cipher} is still on the wire");
        }

        [TestMethod]
        public void TheRetiredMacsAreNoLongerOffered()
        {
            var offered = BuildJumpHostConnection().HmacAlgorithms.Keys.ToList();

            foreach (var mac in RetiredMacs)
                CollectionAssert.DoesNotContain(offered, mac, $"{mac} is still on the wire");
        }

        /// <summary>
        /// DSA is capped at 1024 bits, OpenSSH disabled it by default in 7.0 and removed it outright in 10.0.
        /// SSH.NET dropped it in 2025.0.0.
        /// </summary>
        [TestMethod]
        public void SshDssIsNoLongerAcceptedAsAHostKey()
        {
            var offered = BuildJumpHostConnection().HostKeyAlgorithms.Keys.ToList();

            CollectionAssert.DoesNotContain(offered, "ssh-dss");
        }

        /// <summary>
        /// The Terrapin mitigation (OpenSSH's strict key exchange extension) only engages when both peers can
        /// use an AEAD cipher or an encrypt-then-MAC algorithm. 2023.0.0 offered neither, so the mitigation
        /// could never take effect however well configured the server was.
        /// </summary>
        [TestMethod]
        public void AnAeadCipherIsOfferedSoStrictKeyExchangeCanEngage()
        {
            var offered = BuildJumpHostConnection().Encryptions.Keys.ToList();

            CollectionAssert.Contains(offered, "aes256-gcm@openssh.com");
            CollectionAssert.Contains(offered, "aes128-gcm@openssh.com");
            CollectionAssert.Contains(offered, "chacha20-poly1305@openssh.com");
        }

        [TestMethod]
        public void EncryptThenMacVariantsAreOffered()
        {
            var offered = BuildJumpHostConnection().HmacAlgorithms.Keys.ToList();

            CollectionAssert.Contains(offered, "hmac-sha2-256-etm@openssh.com");
            CollectionAssert.Contains(offered, "hmac-sha2-512-etm@openssh.com");
        }

        /// <summary>
        /// A harvest-now-decrypt-later recording of an SSH session is only as durable as its key exchange.
        /// Both hybrids are what current OpenSSH prefers.
        /// </summary>
        [TestMethod]
        public void APostQuantumKeyExchangeIsOffered()
        {
            var offered = BuildJumpHostConnection().KeyExchangeAlgorithms.Keys.ToList();

            CollectionAssert.Contains(offered, "mlkem768x25519-sha256");
            CollectionAssert.Contains(offered, "sntrup761x25519-sha512");
        }

        /// <summary>
        /// The modern algorithms have to be *preferred*, not merely present: SSH.NET sends the dictionary in
        /// order and the server picks the first name it also knows. CBC below CTR and AEAD is the whole point.
        /// </summary>
        [TestMethod]
        public void TheModernCiphersComeBeforeTheCbcOnes()
        {
            var offered = BuildJumpHostConnection().Encryptions.Keys.ToList();

            var firstCbc = offered.FindIndex(x => x.EndsWith("-cbc", StringComparison.Ordinal));
            Assert.IsTrue(firstCbc > 0, "CBC is still offered, so its position is what matters");

            foreach (var modern in new[] { "aes256-ctr", "aes256-gcm@openssh.com", "chacha20-poly1305@openssh.com" })
                Assert.IsTrue(offered.IndexOf(modern) < firstCbc, $"{modern} is offered after a CBC cipher");
        }

        /// <summary>
        /// Guards the package reference itself. GHSA-q939-rpr3-3284 (CVE-2026-48798) is fixed in 2026.0.0 and
        /// has no configuration workaround, so a downgrade past this line silently reopens it.
        /// </summary>
        [TestMethod]
        public void ThePackageIsAtLeastTheReleaseThatFixedTheScpAdvisory()
        {
            var version = typeof(ConnectionInfo).Assembly.GetName().Version;

            Assert.IsNotNull(version);
            Assert.IsTrue(version!.Major >= 2026, $"SSH.NET {version} predates the GHSA-q939-rpr3-3284 fix");
        }

        /// <summary>
        /// The dictionaries are per-connection copies in SSH.NET, but a shared default would make one
        /// session's tightening leak into another's; if that ever changes the tests above stop meaning
        /// anything for the second connection.
        /// </summary>
        [TestMethod]
        public void EachConnectionGetsItsOwnAlgorithmLists()
        {
            var first = BuildJumpHostConnection();
            var second = BuildJumpHostConnection();

            Assert.AreNotSame(first.Encryptions, second.Encryptions);

            var before = new List<string>(second.Encryptions.Keys);
            first.Encryptions.Clear();

            CollectionAssert.AreEqual(before, second.Encryptions.Keys.ToList());
        }
    }
}
