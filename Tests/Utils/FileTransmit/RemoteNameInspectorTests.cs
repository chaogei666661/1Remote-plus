using _1RM.Utils.FileTransmit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.FileTransmit
{
    /// <summary>
    /// The file browser draws names the server chose, and double-clicking one downloads it and hands it to
    /// ShellExecute. ShellExecute reads the real extension; the screen shows whatever the name's formatting
    /// characters make it show. These cases fix where that line falls.
    /// </summary>
    [TestClass]
    public class RemoteNameInspectorTests
    {
        [TestInitialize]
        public void Setup() => TestInit.Init();

        /// <summary>
        /// The classic: U+202E RIGHT-TO-LEFT OVERRIDE makes the tail render backwards, so a file that
        /// Windows will open as a program is drawn as "invoiceexe.png".
        /// </summary>
        private const string RightToLeftOverrideExe = "invoice\u202Egnp.exe";

        [TestMethod]
        public void AnOrdinaryNameIsNotDeceptive()
        {
            Assert.IsFalse(RemoteNameInspector.IsDeceptive("report.pdf"));
            Assert.IsFalse(RemoteNameInspector.IsDeceptive("Ünïcödé 文件 名.txt"));
            Assert.IsFalse(RemoteNameInspector.IsDeceptive("setup.exe"), "an honest .exe is not a disguise");
            Assert.IsFalse(RemoteNameInspector.IsDeceptive(""));
            Assert.IsFalse(RemoteNameInspector.IsDeceptive(null));
        }

        /// <summary>
        /// These are visible as a gap, which is what they claim to be, so flagging them would be a false
        /// alarm on names that merely came from a word processor.
        /// </summary>
        [TestMethod]
        public void UnusualButVisibleSpacesAreNotDeceptive()
        {
            Assert.IsFalse(RemoteNameInspector.IsDeceptive("annual\u00A0report.pdf"));
            Assert.IsFalse(RemoteNameInspector.IsDeceptive("annual\u3000report.pdf"));
        }

        [TestMethod]
        public void ABidiOverrideIsDeceptive()
        {
            Assert.IsTrue(RemoteNameInspector.IsDeceptive(RightToLeftOverrideExe));
            Assert.IsTrue(RemoteNameInspector.IsDeceptive("photo\u202Bfdp.exe"));
            Assert.IsTrue(RemoteNameInspector.IsDeceptive("photo\u2066fdp.exe"), "the isolates work the same way");
        }

        [TestMethod]
        public void ZeroWidthAndControlCharactersAreDeceptive()
        {
            Assert.IsTrue(RemoteNameInspector.IsDeceptive("read\u200Bme.txt"), "zero-width space");
            Assert.IsTrue(RemoteNameInspector.IsDeceptive("read\u200Dme.txt"), "zero-width joiner");
            Assert.IsTrue(RemoteNameInspector.IsDeceptive("read\uFEFFme.txt"), "byte order mark");
            Assert.IsTrue(RemoteNameInspector.IsDeceptive("read\u0007me.txt"), "a bell would sound, not show");
            Assert.IsTrue(RemoteNameInspector.IsDeceptive("read\nme.txt"), "a newline would break the row");
        }

        [TestMethod]
        public void TheDisplayTextSpellsOutWhatCannotBeSeen()
        {
            Assert.AreEqual("invoice<U+202E>gnp.exe", RemoteNameInspector.ToDisplayText(RightToLeftOverrideExe));
            Assert.AreEqual("read<U+200B>me.txt", RemoteNameInspector.ToDisplayText("read\u200Bme.txt"));
        }

        [TestMethod]
        public void TheDisplayTextLeavesAnOrdinaryNameExactlyAsItIs()
        {
            Assert.AreEqual("report.pdf", RemoteNameInspector.ToDisplayText("report.pdf"));
            Assert.AreEqual("Ünïcödé 文件 名.txt", RemoteNameInspector.ToDisplayText("Ünïcödé 文件 名.txt"));
            Assert.AreEqual("", RemoteNameInspector.ToDisplayText(null));
        }

        /// <summary>
        /// The point of the whole class: the extension the user is warned about has to be the one the shell
        /// will use, not the one the name renders as.
        /// </summary>
        [TestMethod]
        public void TheEffectiveExtensionIgnoresTheFormattingCharacters()
        {
            Assert.AreEqual(".exe", RemoteNameInspector.EffectiveExtension(RightToLeftOverrideExe));
            Assert.AreEqual(".txt", RemoteNameInspector.EffectiveExtension("read\u200Bme.txt"));
            Assert.AreEqual(".exe", RemoteNameInspector.EffectiveExtension("setup.EXE"));
        }

        [TestMethod]
        public void ANameWithNoExtensionHasNoEffectiveExtension()
        {
            Assert.AreEqual("", RemoteNameInspector.EffectiveExtension("Makefile"));
            Assert.AreEqual("", RemoteNameInspector.EffectiveExtension(".bashrc"), "a leading dot hides a file, it does not name a type");
            Assert.AreEqual("", RemoteNameInspector.EffectiveExtension("archive."), "a trailing dot is stripped by Windows");
            Assert.AreEqual("", RemoteNameInspector.EffectiveExtension(""));
            Assert.AreEqual("", RemoteNameInspector.EffectiveExtension(null));
        }

        [TestMethod]
        public void OnlyTheLastExtensionCounts()
        {
            Assert.AreEqual(".exe", RemoteNameInspector.EffectiveExtension("invoice.pdf.exe"));
            Assert.AreEqual(".gz", RemoteNameInspector.EffectiveExtension("logs.tar.gz"));
        }
    }
}
