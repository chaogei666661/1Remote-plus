using _1RM.Utils.FileTransmit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.FileTransmit
{
    /// <summary>
    /// The SFTP/FTP browser's Back button and its path box. Both used to answer the top level wrong, and
    /// only one of the two showed: the Back button's empty-string answer was rescued further down by the
    /// listing call turning an empty path into the root, while <c>/foo/..</c> typed into the path box
    /// really did leave you in <c>/foo</c>.
    /// </summary>
    [TestClass]
    public class RemotePathTests
    {
        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
        }

        [TestMethod]
        public void TheParentOfAFolderIsTheFolderAboveIt()
        {
            Assert.AreEqual("/a/b", RemotePath.Parent("/a/b/c"));
            Assert.AreEqual("/a", RemotePath.Parent("/a/b"));
        }

        /// <summary>
        /// The reported one. <c>"/foo".Substring(0, "/foo".LastIndexOf('/'))</c> is the empty string, not
        /// <c>/</c>.
        /// </summary>
        [TestMethod]
        public void TheParentOfATopLevelFolderIsTheRootAndNotNowhere()
        {
            Assert.AreEqual("/", RemotePath.Parent("/foo"));
            Assert.AreEqual("/", RemotePath.Parent("/a"));
        }

        [TestMethod]
        public void TheRootHasNoFolderAboveItAndSaysSoRatherThanEmptying()
        {
            Assert.AreEqual("/", RemotePath.Parent("/"));
            Assert.AreEqual("/", RemotePath.Parent(""));
            Assert.AreEqual("/", RemotePath.Parent(null));
        }

        /// <summary>
        /// <c>/a/b/</c> and <c>/a/b</c> are one folder, so they have one parent. Without the trim the
        /// trailing separator is the last one found and the "parent" of <c>/a/b/</c> is <c>/a/b</c>.
        /// </summary>
        [TestMethod]
        public void ATrailingSeparatorDoesNotCostALevel()
        {
            Assert.AreEqual("/a", RemotePath.Parent("/a/b/"));
            Assert.AreEqual("/", RemotePath.Parent("/a/"));
        }

        [TestMethod]
        public void AnEmptyPathIsTheRoot()
        {
            Assert.AreEqual("/", RemotePath.Resolve(null));
            Assert.AreEqual("/", RemotePath.Resolve(""));
            Assert.AreEqual("/", RemotePath.Resolve("   "));
        }

        /// <summary>
        /// The visible half of the bug. The old resolution took the last separator and then refused to use
        /// it with <c>if (i &gt; 0)</c> - and for a top-level folder the only separator there is sits at
        /// index 0, so <c>/foo/..</c> resolved to <c>/foo</c>.
        /// </summary>
        [TestMethod]
        public void ADotDotTypedIntoThePathBoxGoesUpEvenFromTheTopLevel()
        {
            Assert.AreEqual("/", RemotePath.Resolve("/foo/.."));
            Assert.AreEqual("/a", RemotePath.Resolve("/a/b/.."));
            Assert.AreEqual("/", RemotePath.Resolve("/.."));
        }

        [TestMethod]
        public void SeveralDotDotsInARowEachTakeALevel()
        {
            Assert.AreEqual("/", RemotePath.Resolve("/a/b/../.."));
            Assert.AreEqual("/a", RemotePath.Resolve("/a/b/c/../.."));
            // Past the root is still the root, not an empty path or a climb into nothing.
            Assert.AreEqual("/", RemotePath.Resolve("/a/../../../.."));
        }

        /// <summary>
        /// Only a run of them on the end is applied. <c>..</c> is a legal name on a POSIX server when it is
        /// not a whole path segment, and a segment in the middle is the server's to interpret - resolving
        /// it here would send a listing request for a folder the user did not ask for.
        /// </summary>
        [TestMethod]
        public void ADotDotThatIsNotOnTheEndIsLeftForTheServer()
        {
            Assert.AreEqual("/a/../b", RemotePath.Resolve("/a/../b"));
            Assert.AreEqual("/a/..b", RemotePath.Resolve("/a/..b"));
            Assert.AreEqual("/a/b..", RemotePath.Resolve("/a/b.."));
        }

        [TestMethod]
        public void AnOrdinaryPathIsHandedOverUnchanged()
        {
            Assert.AreEqual("/var/log", RemotePath.Resolve("/var/log"));
            Assert.AreEqual("/", RemotePath.Resolve("/"));
        }
    }
}
