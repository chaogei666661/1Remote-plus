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

        /// <summary>
        /// Lists a real tree, except for the folders named here, which fail the way an unreadable one does.
        ///
        /// A test cannot make a genuinely unreadable directory on both platforms: Unix wants a chmod that
        /// the root account then ignores, and Windows wants an ACL edit through an API that exists nowhere
        /// else. So the file system is real and only the refusal is staged.
        /// </summary>
        private sealed class RefusingLister : ILocalDirectoryLister
        {
            private readonly HashSet<string> _refusedNames;
            private readonly Exception _failure;

            public RefusingLister(Exception failure, params string[] refusedNames)
            {
                _failure = failure;
                _refusedNames = new HashSet<string>(refusedNames, StringComparer.Ordinal);
            }

            /// <summary>Names whose <see cref="ILocalDirectoryLister.GetFiles"/> works even so.</summary>
            public HashSet<string> FilesStillReadable { get; } = new HashSet<string>(StringComparer.Ordinal);

            public DirectoryInfo[] GetDirectories(DirectoryInfo directory)
            {
                if (_refusedNames.Contains(directory.Name))
                    throw _failure;
                return directory.GetDirectories();
            }

            public FileInfo[] GetFiles(DirectoryInfo directory)
            {
                if (_refusedNames.Contains(directory.Name) && !FilesStillReadable.Contains(directory.Name))
                    throw _failure;
                return directory.GetFiles();
            }
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
            // A colon past the drive qualifier: a folder name on Unix, an alternate data stream to Win32,
            // and a folder the server cannot be asked to create either way. Both shapes are checked here
            // because this is string work and gives the same answer on whichever platform runs it.
            Assert.AreEqual("", LocalUploadScan.RemoteFolderName(@"C:\Users\bob\stream:evil"));
            Assert.AreEqual("", LocalUploadScan.RemoteFolderName("/home/bob/stream:evil"));
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

        // ---------------------------------------------------------------- folders that will not be listed

        /// <summary>
        /// The bug this section is about. One folder the walk was not allowed to list threw
        /// UnauthorizedAccessException out of the whole Enumerate call, into a catch in TransmitTask that
        /// only logs — so the upload sent nothing whatsoever and reported success. A drive root reaches this
        /// on any Windows machine (System Volume Information, $Recycle.Bin), and so does any folder with one
        /// other account's directory below it.
        /// </summary>
        [TestMethod]
        public void OneFolderThatCannotBeListedDoesNotCostTheWholeUpload()
        {
            var top = Dir("proj");
            Dir("proj", "locked");
            Dir("proj", "src");
            File_("x", "proj", "readme.md");
            File_("x", "proj", "src", "main.cs");
            File_("secret", "proj", "locked", "inside.txt");

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top),
                new RefusingLister(new UnauthorizedAccessException("access denied"), "locked"));

            CollectionAssert.AreEqual(
                new[] { "proj", "proj/locked", "proj/readme.md", "proj/src", "proj/src/main.cs" },
                RelativePaths(result));
            CollectionAssert.AreEqual(new[] { "proj/locked" }, result.FoldersNotRead.ToArray());
            Assert.IsFalse(result.Entries.Any(x => x.RelativePath.EndsWith("inside.txt")));
        }

        [TestMethod]
        public void AnUnreadableFolderIsStillCreatedOnTheServer()
        {
            var top = Dir("proj");
            Dir("proj", "locked");

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top),
                new RefusingLister(new UnauthorizedAccessException(), "locked"));

            var entry = result.Entries.Single(x => x.RelativePath == "proj/locked");
            Assert.IsTrue(entry.IsDirectory, "the folder exists, so leaving it out of the tree is a second lie");
        }

        /// <summary>
        /// The chosen folder itself. Nothing below it can be listed, so the upload is one empty folder — but
        /// it still has to be an upload that says why, rather than an exception nobody sees.
        /// </summary>
        [TestMethod]
        public void AChosenFolderThatCannotBeListedIsReportedRatherThanThrown()
        {
            var top = Dir("proj");
            File_("x", "proj", "readme.md");

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top),
                new RefusingLister(new UnauthorizedAccessException(), "proj"));

            CollectionAssert.AreEqual(new[] { "proj" }, RelativePaths(result));
            CollectionAssert.AreEqual(new[] { "proj" }, result.FoldersNotRead.ToArray());
        }

        /// <summary>
        /// A folder is deleted, or a network share goes away, while the scan is running. Same handling: skip
        /// the folder, keep the transfer.
        /// </summary>
        [TestMethod]
        public void AFolderThatDisappearsMidScanIsSkippedNotFatal()
        {
            var top = Dir("proj");
            Dir("proj", "gone");
            File_("x", "proj", "kept.txt");

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top),
                new RefusingLister(new DirectoryNotFoundException(), "gone"));

            Assert.IsTrue(result.Entries.Any(x => x.RelativePath == "proj/kept.txt"));
            CollectionAssert.AreEqual(new[] { "proj/gone" }, result.FoldersNotRead.ToArray());
        }

        [TestMethod]
        public void EveryUnreadableFolderIsNamedAndNoneTwice()
        {
            var top = Dir("proj");
            Dir("proj", "a");
            Dir("proj", "b");
            Dir("proj", "c");

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top),
                new RefusingLister(new UnauthorizedAccessException(), "a", "b"));

            CollectionAssert.AreEquivalent(new[] { "proj/a", "proj/b" }, result.FoldersNotRead.ToArray());
        }

        /// <summary>
        /// Listing subfolders and listing files are two calls, and a folder can refuse one and answer the
        /// other. Whatever came back is still worth uploading.
        /// </summary>
        [TestMethod]
        public void AFolderThatRefusesOnlyItsSubfoldersStillSendsItsFiles()
        {
            var top = Dir("proj");
            Dir("proj", "half", "deep");
            File_("x", "proj", "half", "visible.txt");

            var lister = new RefusingLister(new UnauthorizedAccessException(), "half");
            lister.FilesStillReadable.Add("half");

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top), lister);

            Assert.IsTrue(result.Entries.Any(x => x.RelativePath == "proj/half/visible.txt"));
            Assert.IsFalse(result.Entries.Any(x => x.RelativePath.Contains("deep")));
            CollectionAssert.AreEqual(new[] { "proj/half" }, result.FoldersNotRead.ToArray());
        }

        [TestMethod]
        public void AFolderThatListsFineIsNotReportedAsUnread()
        {
            var top = Dir("proj");
            Dir("proj", "src");
            File_("x", "proj", "src", "main.cs");

            var result = LocalUploadScan.Enumerate(new DirectoryInfo(top));

            Assert.AreEqual(0, result.FoldersNotRead.Count);
        }

        /// <summary>
        /// Only the failures that are about one folder are absorbed. An OutOfMemoryException is about the
        /// process, and pretending the folder was merely unreadable would upload a tree with holes in it.
        /// </summary>
        [TestMethod]
        public void AFailureThatIsNotAboutThisFolderStillEndsTheScan()
        {
            var top = Dir("proj");
            Dir("proj", "boom");

            Assert.ThrowsException<OutOfMemoryException>(() =>
                LocalUploadScan.Enumerate(new DirectoryInfo(top),
                    new RefusingLister(new OutOfMemoryException(), "boom")));
        }

        // ---------------------------------------------------------------- refusals

        /// <summary>
        /// A folder the scan cannot name has to be refused with an exception, not walked anyway.
        ///
        /// The input matters more than it looks. This case used to pass the separator on its own, which is
        /// only unnameable on Unix: on Windows <c>\</c> resolves to the root of the current drive, and a
        /// drive root is nameable on purpose — see ADriveRootIsNamedAfterItsDriveInsteadOfFailing. So the
        /// walk started, listing all of <c>C:\</c> raised UnauthorizedAccessException, and the case failed
        /// on CI while passing here.
        ///
        /// A colon in the last component is refused on both platforms and survives path normalisation on
        /// both: Unix allows a folder to be called that, Windows reads it as an alternate data stream, and
        /// neither is a folder the server can be asked to create. The path need not exist — the name is
        /// decided before anything is listed.
        /// </summary>
        [TestMethod]
        public void AFolderThatCannotBeNamedOnTheServerIsRefusedLoudly()
        {
            var unnameable = new DirectoryInfo(Path.Combine(_root, "stream:evil"));

            Assert.AreEqual("", LocalUploadScan.RemoteFolderName(unnameable),
                "this case is only worth anything if the platform really cannot name the input");
            Assert.ThrowsException<ArgumentException>(() => LocalUploadScan.Enumerate(unnameable));
        }
    }
}
