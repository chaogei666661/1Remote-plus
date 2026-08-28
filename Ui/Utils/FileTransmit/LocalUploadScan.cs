using System;
using System.Collections.Generic;
using System.IO;

namespace _1RM.Utils.FileTransmit
{
    /// <summary>
    /// One local file or folder an upload is going to send, and where it goes relative to the remote
    /// directory the user is standing in.
    /// </summary>
    public sealed class LocalUploadEntry
    {
        public LocalUploadEntry(FileSystemInfo info, bool isDirectory, string relativePath)
        {
            Info = info;
            IsDirectory = isDirectory;
            RelativePath = relativePath;
        }

        public FileSystemInfo Info { get; }

        public bool IsDirectory { get; }

        /// <summary>
        /// Where this entry goes below the remote directory, always with <c>/</c> separators and never
        /// leading with one. <c>proj</c>, <c>proj/src</c>, <c>proj/src/main.cs</c>.
        /// </summary>
        public string RelativePath { get; }
    }

    /// <summary>The outcome of walking one chosen local folder.</summary>
    public sealed class LocalUploadScanResult
    {
        public LocalUploadScanResult(IReadOnlyList<LocalUploadEntry> entries, IReadOnlyList<string> linksNotFollowed)
        {
            Entries = entries;
            LinksNotFollowed = linksNotFollowed;
        }

        public IReadOnlyList<LocalUploadEntry> Entries { get; }

        /// <summary>
        /// Relative paths of directory links that were listed but not walked into. The user is told:
        /// silently sending an empty folder where they can see a full one is its own kind of wrong.
        /// </summary>
        public IReadOnlyList<string> LinksNotFollowed { get; }
    }

    /// <summary>
    /// Walks a local folder chosen for upload.
    ///
    /// The download direction has always refused to recurse through a symlink the server reported
    /// (<c>item.IsDirectory &amp;&amp; !item.IsSymlink</c>). The upload direction did not do the mirror of
    /// that: it called <see cref="DirectoryInfo.GetDirectories()"/> and walked into whatever came back,
    /// junctions and symlinks included. Two things follow, and neither needs a hostile server — an ordinary
    /// Windows profile is full of reparse points:
    ///
    /// <list type="bullet">
    /// <item>A link that points at an ancestor makes the walk re-enter the tree it is already in, at a
    /// longer path each time. What stops it is whatever the platform runs out of first — on Unix the
    /// kernel's symlink limit, measured here at 124 phantom entries and 376-character paths from a
    /// three-file folder; on Windows a directory junction has no such counter and the walk keeps going
    /// until the path length does. Whichever it is arrives as an exception into a <c>catch</c> that only
    /// logs, so the transfer sits in <c>Scanning</c>, then uploads nothing, and says nothing.</item>
    /// <item>A junction that points somewhere else entirely — <c>AppData</c>, a mapped drive, the whole of
    /// <c>C:\</c> — is uploaded to the remote server along with the folder the user actually picked. That
    /// is a data-disclosure decision being made by a link the user probably did not know was there.</item>
    /// </list>
    ///
    /// So a directory link is listed, and created empty on the far side, but never descended into. A file
    /// link is left alone: reading through it is what any copy of that file does, it cannot loop, and it
    /// cannot pull in more than the one file the user can see.
    /// </summary>
    public static class LocalUploadScan
    {
        private static readonly char[] Separators = { '/', '\\' };

        /// <summary>
        /// Whether this entry is a symlink, a junction, or another reparse point. Both tests are kept:
        /// <see cref="FileSystemInfo.LinkTarget"/> is the modern one and answers for symlinks on every
        /// platform, and the attribute catches the reparse points that are not links in that sense —
        /// OneDrive placeholders and dedup stubs among them.
        /// </summary>
        public static bool IsLink(FileSystemInfo info)
        {
            try
            {
                if (info.LinkTarget != null)
                    return true;
                return (info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch (Exception)
            {
                // Something we cannot even read the attributes of is not something to walk into.
                return true;
            }
        }

        /// <summary>
        /// The name a chosen local folder takes on the server.
        ///
        /// This used to be <c>topDirectory.Parent!.FullName</c>, with everything below it built by cutting
        /// that prefix off each child. A drive root and a UNC share root have no parent, so picking one
        /// threw a <see cref="NullReferenceException"/> into a <c>catch</c> that logs and returns — dragging
        /// a whole drive onto the panel did nothing at all, without a message.
        /// </summary>
        /// <returns>A single path component, or an empty string for a path with no nameable component.</returns>
        public static string RemoteFolderName(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return "";

            var trimmed = fullPath.TrimEnd(Separators);
            if (trimmed.Length == 0)
                return "";

            var cut = trimmed.LastIndexOfAny(Separators);
            var name = cut >= 0 ? trimmed.Substring(cut + 1) : trimmed;

            // `D:\` trims to `D:`, which is a drive qualifier and not a name. `D` is what the user calls it.
            if (name.Length == 2 && name[1] == ':' && char.IsLetter(name[0]))
                return name.Substring(0, 1);

            return DownloadPathGuard.IsSafeSegment(name) ? name : "";
        }

        /// <inheritdoc cref="RemoteFolderName(string)"/>
        public static string RemoteFolderName(DirectoryInfo directory)
        {
            return RemoteFolderName(directory.FullName);
        }

        /// <summary>
        /// Every file and folder under <paramref name="topDirectory"/>, breadth first, with the directory
        /// links listed but not followed.
        /// </summary>
        /// <exception cref="ArgumentException">The folder has no name that can be used on the server.</exception>
        public static LocalUploadScanResult Enumerate(DirectoryInfo topDirectory)
        {
            if (topDirectory == null)
                throw new ArgumentNullException(nameof(topDirectory));

            var rootName = RemoteFolderName(topDirectory);
            if (rootName.Length == 0)
                throw new ArgumentException($"cannot name '{topDirectory.FullName}' on the server", nameof(topDirectory));

            var entries = new List<LocalUploadEntry>();
            var linksNotFollowed = new List<string>();
            var pending = new Queue<KeyValuePair<DirectoryInfo, string>>();

            entries.Add(new LocalUploadEntry(topDirectory, true, rootName));
            pending.Enqueue(new KeyValuePair<DirectoryInfo, string>(topDirectory, rootName));

            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                var directory = current.Key;
                var prefix = current.Value;

                foreach (var sub in directory.GetDirectories())
                {
                    // A local name is one path component by construction, so a separator or a `..` in one
                    // would mean the platform handed back something that cannot be true. Refusing it costs
                    // nothing and keeps the remote path a function of names we have looked at.
                    if (!DownloadPathGuard.IsSafeSegment(sub.Name))
                        continue;

                    var relative = prefix + "/" + sub.Name;
                    entries.Add(new LocalUploadEntry(sub, true, relative));

                    if (IsLink(sub))
                        linksNotFollowed.Add(relative);
                    else
                        pending.Enqueue(new KeyValuePair<DirectoryInfo, string>(sub, relative));
                }

                foreach (var file in directory.GetFiles())
                {
                    if (!DownloadPathGuard.IsSafeSegment(file.Name))
                        continue;
                    entries.Add(new LocalUploadEntry(file, false, prefix + "/" + file.Name));
                }
            }

            return new LocalUploadScanResult(entries, linksNotFollowed);
        }
    }
}
