using _1RM.Utils.RdpFile;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.RdpFile
{
    /// <summary>
    /// The name of a generated <c>.rdp</c> comes from the server's display name, which is a free-text field
    /// the user types and which a shared data source lets somebody else write. These cases are the shapes
    /// that used to produce a path Win32 could not hold, a file the caller could not find again, or - for
    /// the export action, which stripped nothing at all - a save dialog that simply refused.
    /// </summary>
    [TestClass]
    public class RdpFileNameTests
    {
        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
        }

        [TestMethod]
        public void AnOrdinaryNameIsLeftAlone()
        {
            Assert.AreEqual("web01.rdp", RdpFileName.Make("web01"));
            Assert.AreEqual("web01-dmz_3389_ab12.rdp", RdpFileName.ForSession("web01-dmz", "3389", "ab12"));
        }

        /// <summary>
        /// The case the export action was reported for: <c>web01 / dmz</c> has a directory separator in it,
        /// so the save dialog was handed a path with a folder that does not exist.
        /// </summary>
        [TestMethod]
        public void ASeparatorInTheDisplayNameCannotReachThePath()
        {
            Assert.AreEqual("web01  dmz.rdp", RdpFileName.Make("web01 / dmz"));
            Assert.AreEqual("srvC.rdp", RdpFileName.Make(@"srv\C"));
        }

        [TestMethod]
        public void EveryCharacterWin32RefusesIsRemoved()
        {
            // Deliberately not Path.GetInvalidFileNameChars(): on Linux that is '/' and NUL only, and this
            // assertion has to mean the same thing wherever it runs.
            Assert.AreEqual("srv.rdp", RdpFileName.Make("<s>r:\"v/\\|?*"));
        }

        [TestMethod]
        public void AControlCharacterIsRemovedToo()
        {
            Assert.AreEqual("srvdmz.rdp", RdpFileName.Make("srv\r\ndmz"));
            Assert.AreEqual("srvdmz.rdp", RdpFileName.Make("srv\0dmz"));
        }

        /// <summary>
        /// Nothing usable left. Without a fallback the file is called <c>.rdp</c>, which is an extension
        /// with no name and is hidden from the user in most places that would show it.
        /// </summary>
        [TestMethod]
        public void ANameThatSanitisesAwayToNothingStillGetsAName()
        {
            Assert.AreEqual("rdp.rdp", RdpFileName.Make(""));
            Assert.AreEqual("rdp.rdp", RdpFileName.Make(null));
            Assert.AreEqual("rdp.rdp", RdpFileName.Make("///"));
            Assert.AreEqual("rdp.rdp", RdpFileName.Make("   "));
            Assert.AreEqual("rdp.rdp", RdpFileName.Make("..."));
        }

        /// <summary>
        /// Win32 stores neither a trailing space nor a trailing dot, so a caller that kept the name it asked
        /// for is holding a path that does not refer to the file that was created.
        /// </summary>
        [TestMethod]
        public void ATrailingSpaceOrDotIsDroppedHereRatherThanByWin32()
        {
            Assert.AreEqual("srv.rdp", RdpFileName.Make("srv. "));
            Assert.AreEqual("srv.rdp", RdpFileName.Make("srv..."));
            Assert.AreEqual("srv.rdp", RdpFileName.Make("  srv  "));
        }

        /// <summary>
        /// <c>CON</c>, <c>LPT1</c> and the rest still name a DOS device even with an extension after them,
        /// so <c>File.WriteAllText</c> on <c>CON.rdp</c> writes to the console and leaves no file.
        /// </summary>
        [TestMethod]
        public void ADeviceNameIsNotUsedAsAFileName()
        {
            Assert.AreEqual("_CON.rdp", RdpFileName.Make("CON"));
            Assert.AreEqual("_con.rdp", RdpFileName.Make("con"));
            Assert.AreEqual("_LPT1.rdp", RdpFileName.Make("LPT1"));
            Assert.AreEqual("_nul.rdp", RdpFileName.Make("nul"));
            // Only the whole stem is a device; a name that merely starts with one is a normal file.
            Assert.AreEqual("console.rdp", RdpFileName.Make("console"));
        }

        [TestMethod]
        public void AVeryLongDisplayNameCannotPushThePathPastWhatWin32Accepts()
        {
            var name = RdpFileName.Make(new string('x', 400));

            Assert.AreEqual(RdpFileName.MAX_STEM_LENGTH + RdpFileName.EXTENSION.Length, name.Length);
            Assert.IsTrue(name.EndsWith(RdpFileName.EXTENSION));
        }

        /// <summary>
        /// The port and the discriminator are trimmed away with everything else when the stem is too long,
        /// which is fine - the directory the connect path writes into is already unique per invocation - but
        /// the extension has to survive or mstsc will not open the file.
        /// </summary>
        [TestMethod]
        public void TheExtensionSurvivesTheLengthCap()
        {
            var name = RdpFileName.ForSession(new string('y', 300), "3389", "ab12");

            Assert.IsTrue(name.EndsWith(RdpFileName.EXTENSION));
            Assert.IsTrue(name.Length <= RdpFileName.MAX_STEM_LENGTH + RdpFileName.EXTENSION.Length);
        }

        /// <summary>
        /// A shell metacharacter is a legal Win32 file name character and is kept: refusing it would rename
        /// an <c>R&amp;D box</c> behind the user's back. What must not happen is the name reaching a shell,
        /// which is <see cref="RdpFilePreview"/>'s job rather than this one's.
        /// </summary>
        [TestMethod]
        public void AShellMetacharacterIsAFileNameCharacterAndIsKept()
        {
            Assert.AreEqual("R&D box.rdp", RdpFileName.Make("R&D box"));
            Assert.AreEqual("x&calc&y.rdp", RdpFileName.Make("x&calc&y"));
            Assert.AreEqual("100%.rdp", RdpFileName.Make("100%"));
        }

        [TestMethod]
        public void AMissingPortOrUserStillProducesAUsableName()
        {
            Assert.AreEqual("srv__.rdp", RdpFileName.ForSession("srv", null, null));
            Assert.AreEqual("__.rdp", RdpFileName.ForSession(null, null, null));
        }
    }
}
