using _1RM.Utils.RdpFile;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.RdpFile
{
    /// <summary>
    /// The <c>password 51:b:</c> line of a .rdp file.
    ///
    /// It used to be produced with <c>CRYPTPROTECT_LOCAL_MACHINE</c>, which keys the blob to the machine
    /// instead of to the user: any other account on that PC, and any service running on it, could call
    /// CryptUnprotectData on the file and read the password back. mstsc protects a saved password with the
    /// user's key, which is why an .rdp somebody else opens simply prompts. The flags are asserted here
    /// rather than the ciphertext because DPAPI needs Windows, and because reintroducing the flag is a
    /// one-character change that nothing else would notice.
    /// </summary>
    [TestClass]
    public class RdpConfigPasswordTests
    {
        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x01;
        private const int CRYPTPROTECT_LOCAL_MACHINE = 0x04;

        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
        }

        [TestMethod]
        public void ThePasswordIsNotProtectedWithTheMachineKey()
        {
            Assert.AreEqual(0, RdpConfig.PASSWORD_PROTECTION_FLAGS & CRYPTPROTECT_LOCAL_MACHINE,
                "CRYPTPROTECT_LOCAL_MACHINE would let every other account on this PC decrypt the saved password");
        }

        [TestMethod]
        public void ProtectingThePasswordNeverPutsAPromptOnTheScreen()
        {
            // A .rdp is written on the connect path and on export, neither of which can answer a modal
            // dialog raised from inside crypt32.
            Assert.AreEqual(CRYPTPROTECT_UI_FORBIDDEN,
                RdpConfig.PASSWORD_PROTECTION_FLAGS & CRYPTPROTECT_UI_FORBIDDEN);
        }

        [TestMethod]
        public void TheFlagsAreExactlyWhatMstscUses()
        {
            Assert.AreEqual(CRYPTPROTECT_UI_FORBIDDEN, RdpConfig.PASSWORD_PROTECTION_FLAGS);
        }

        [TestMethod]
        public void TheBlobIsWrittenAsUnbrokenUppercaseHex()
        {
            Assert.AreEqual("00FF10AB", RdpConfig.EncodePassword(new byte[] { 0x00, 0xFF, 0x10, 0xAB }));
        }

        /// <summary>
        /// DPAPI returning null used to reach <c>BitConverter.ToString(null)</c> and take the whole connect
        /// with it. A .rdp without a password line is a prompt, which is a far better outcome than a crash.
        /// </summary>
        [TestMethod]
        public void APasswordThatCouldNotBeProtectedDoesNotTakeTheSessionDown()
        {
            Assert.AreEqual("", RdpConfig.EncodePassword(null));
            Assert.AreEqual("", RdpConfig.EncodePassword(new byte[0]));
        }
    }
}
