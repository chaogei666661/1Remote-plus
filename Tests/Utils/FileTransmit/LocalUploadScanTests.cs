using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using _1RM.Utils.FileTransmit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.FileTransmit
{
    /// <summary>
    /// The upload scan walks a folder the user dropped on the panel. Before this class it followed every
    /// directory the platform listed, reparse points included, so a junction pointing at an ancestor made
    /// the walk endless and a junction pointing at AppData shipped AppData to the remote server.
    ///
    /// These run against a real directory tree with real links, because whether a junction is followed is a
    /// property of the file system and not of a string. Windows makes a junction and Unix makes a symlink;
    /// .NET reports both through LinkTarget and the ReparsePoint attribute, which is what the scan reads.
    /// </summary>
    [TestClass]
    public class LocalUploadScanTests
    {
        private string _root = "";

        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
            _root = Path.Combine(Path.GetTempPath(), "1remote-upload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test run over. A directory containing a
                // link is exactly the case where a recursive delete is most likely to complain.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private string Dir(params string[] parts)
        {
            var path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
            Directory.CreateDirectory(path);
            return path;
        }

        private string File_(string content, params string[] parts)
        {
            var path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        private static List<string> RelativePaths(LocalUploadScanResult result)
        {
            return result.Entries.Select(x => x.RelativePath).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        // ---------------------------------------------------------------- naming the chosen folder

        [TestMethod]
        public void AnOrdinaryFolderKeepsItsOwnName()
        {
            Assert.AreEqual("proj", LocalUploadScan.RemoteFolderName(@"C:\Users\bob\proj"));
            Assert.AreEqual("proj", LocalUploadScan.RemoteFolderName(@"C:\Users\bob\proj\"));
            Assert.AreEqual("proj", LocalUploadScan.RemoteFolderName("/home/bob/proj"));
        }

        /// <summary>
        /// The old code took the parent of the folder being uploaded and cut its path off every child.
        /// A drive root has no parent, so picking one threw a NullReferenceException into a catch that only
        /// logs: dragging a whole drive onto the panel did nothing whatsoever, and said nothing either.
        /// </summary>
        [TestMethod]
        public void ADriveRootIsNamedAfterItsDriveInsteadOfFailing()
        {
            Assert.AreEqual("D", LocalUploadScan.RemoteFolderName(@"D:\"));
            Assert.AreEqual("D", LocalUploadScan.RemoteFolderName(@"D:"));
            Assert.AreEqual("C", LocalUploadScan.RemoteFolderName(@"c:\").ToUpperInvariant());
        }

        [TestMethod]
        public void AUncShareIsNamedAfterTheShare()
        {
            Assert.AreEqual("share", LocalUploadScan.RemoteFolderName(@"\\fileserver\share"));
            Assert.AreEqual("share", LocalUploadScan.RemoteFolderName(@"\\fileserver\share\"));
            Assert.AreEqual("team", LocalUploadScan.RemoteFolderName(@"\\fileserver\share\team"));
        }

        [TestMethod]
        public void APathWithNoNameableComponentIsRefusedRatherThanGuessed()
        {
            Assert.AreEqual("", LocalUploadScan.RemoteFolderName(""));
            Assert.AreEqual("", LocalUploadScan.RemoteFolderName("   "));
            Assert.AreEqual("", LocalUploadScan.RemoteFolderName("/"));
            Assert.AreEqual("", LocalUploadScan.RemoteFolderName(@"\"));
        }

        // ---------------------------------------------------------------- the walk

        [TestMethod]
        public void EveryFileAndFolderIsListedRelativeToTheChosenFolder()
        {
            var top = Dir("proj");
            Dir("proj", "src", "deep");
            File_("x", "proj", "readme.md");
            File_("x", "proj", "src", "main.cs");
            File_("x", "proj", "src", "deep", "note.txt");

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top));

            CollectionAssert.AreEqual(
                new[] { "proj", "proj/readme.md", "proj/src", "proj/src/deep", "proj/src/deep/note.txt", "proj/src/main.cs" },
                RelativePaths(result));
            Assert.AreEqual(0, result.LinksNotFollowed.Count);
        }

        [TestMethod]
        public void ARelativePathNeverUsesTheWindowsSeparator()
        {
            var top = Dir("proj");
            File_("x", "proj", "src", "main.cs");

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top));

            Assert.IsFalse(result.Entries.Any(x => x.RelativePath.Contains('\\')),
                "a remote path is built with '/', whatever the local platform uses");
            Assert.IsFalse(result.Entries.Any(x => x.RelativePath.StartsWith("/")),
                "the path is relative to the remote directory and must not root itself");
        }

        [TestMethod]
        public void ADirectoryIsReportedAsOneAndAFileIsNot()
        {
            var top = Dir("proj");
            File_("x", "proj", "readme.md");

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top));

            Assert.IsTrue(result.Entries.Single(x => x.RelativePath == "proj").IsDirectory);
            Assert.IsFalse(result.Entries.Single(x => x.RelativePath == "proj/readme.md").IsDirectory);
            Assert.IsInstanceOfType(result.Entries.Single(x => x.RelativePath == "proj/readme.md").Info, typeof(FileInfo));
        }

        [TestMethod]
        public void AnEmptyFolderIsStillTheOneEntryTheUploadNeeds()
        {
            var top = Dir("empty");

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top));

            Assert.AreEqual(1, result.Entries.Count);
            Assert.AreEqual("empty", result.Entries[0].RelativePath);
        }

        // ---------------------------------------------------------------- links

        [TestMethod]
        public void ADirectoryLinkIsListedButNotWalkedInto()
        {
            var top = Dir("proj");
            var elsewhere = Dir("elsewhere");
            File_("secret", "elsewhere", "id_rsa");
            Directory.CreateSymbolicLink(Path.Combine(top, "link"), elsewhere);

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top));

            CollectionAssert.AreEqual(new[] { "proj", "proj/link" }, RelativePaths(result));
            CollectionAssert.AreEqual(new[] { "proj/link" }, result.LinksNotFollowed.ToArray());
            Assert.IsFalse(result.Entries.Any(x => x.RelativePath.EndsWith("id_rsa")),
                "a link is exactly how a folder the user did not choose gets uploaded");
        }

        /// <summary>
        /// The reason this is a crash and not only a disclosure. Following the link re-enters the tree at a
        /// longer path each time; with the fix removed, this same three-file folder produces 124 entries and
        /// 376-character paths before the Unix symlink limit stops it, and on Windows a junction has no such
        /// limit at all. The exception that ends it is swallowed, so the upload never happens.
        /// </summary>
        [TestMethod]
        public void ALinkPointingAtAnAncestorDoesNotLoopForever()
        {
            var top = Dir("proj");
            Dir("proj", "src");
            File_("x", "proj", "src", "main.cs");
            Directory.CreateSymbolicLink(Path.Combine(top, "src", "loop"), top);

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top));

            CollectionAssert.AreEqual(
                new[] { "proj", "proj/src", "proj/src/loop", "proj/src/main.cs" },
                RelativePaths(result));
            CollectionAssert.AreEqual(new[] { "proj/src/loop" }, result.LinksNotFollowed.ToArray());
        }

        [TestMethod]
        public void TwoLinksAreBothReported()
        {
            var top = Dir("proj");
            var elsewhere = Dir("elsewhere");
            Directory.CreateSymbolicLink(Path.Combine(top, "a"), elsewhere);
            Directory.CreateSymbolicLink(Path.Combine(top, "b"), elsewhere);

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top));

            CollectionAssert.AreEquivalent(new[] { "proj/a", "proj/b" }, result.LinksNotFollowed.ToArray());
        }

        /// <summary>
        /// A file link is followed on purpose. Reading through it is what copying that file means, it cannot
        /// loop, and it cannot bring in anything beyond the one file the user can already see in the folder.
        /// </summary>
        [TestMethod]
        public void AFileLinkIsStillUploaded()
        {
            var top = Dir("proj");
            var target = File_("contents", "elsewhere", "real.txt");
            System.IO.File.CreateSymbolicLink(Path.Combine(top, "alias.txt"), target);

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top));

            CollectionAssert.AreEqual(new[] { "proj", "proj/alias.txt" }, RelativePaths(result));
            Assert.AreEqual(0, result.LinksNotFollowed.Count, "a file link is not a folder that arrives empty");
        }

        [TestMethod]
        public void AFolderBelowALinkIsNotReachedThroughIt()
        {
            var top = Dir("proj");
            var elsewhere = Dir("elsewhere");
            Dir("elsewhere", "deep");
            File_("x", "elsewhere", "deep", "buried.txt");
            Directory.CreateSymbolicLink(Path.Combine(top, "link"), elsewhere);

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top));

            Assert.IsFalse(result.Entries.Any(x => x.RelativePath.Contains("deep")));
            Assert.IsFalse(result.Entries.Any(x => x.RelativePath.Contains("buried")));
        }

        [TestMethod]
        public void IsLinkAgreesWithTheFileSystem()
        {
            var plain = Dir("plain");
            Directory.CreateSymbolicLink(Path.Combine(_root, "linked"), plain);

            Assert.IsFalse(LocalUploadScan.IsLink(new DirectoryInfo(plain)));
            Assert.IsTrue(LocalUploadScan.IsLink(new DirectoryInfo(Path.Combine(_root, "linked"))));
        }

        // ---------------------------------------------------------------- refusals

        [TestMethod]
        public void AFolderThatCannotBeNamedOnTheServerIsRefusedLoudly()
        {
            Assert.ThrowsException<ArgumentException>(
                () => LocalUploadScan.Enumerate(new DirectoryInfo(Path.DirectorySeparatorChar.ToString())));
        }
    }
}
