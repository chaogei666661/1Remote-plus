using System;

namespace _1RM.Utils.FileTransmit
{
    /// <summary>
    /// The two path questions the SFTP/FTP browser's navigation asks, in one place that can be checked
    /// without a window.
    ///
    /// Both used to be answered inline in <c>VmFileTransmitHost</c> and both got the top level wrong.
    /// <c>CmdGoToParent</c> computed <c>CurrentPath.Substring(0, lastSlash)</c>, which is the empty string
    /// for <c>/foo</c> rather than <c>/</c> — that one never showed, because <c>ShowFolder</c> happens to
    /// turn an empty path into the root on the way in. Typing <c>/foo/..</c> into the path box did show:
    /// the resolution was guarded with <c>if (i &gt; 0)</c>, so the one separator it could find was at
    /// index 0 and nothing was removed. The box went to <c>/foo</c>, which is where you already were.
    /// </summary>
    public static class RemotePath
    {
        public const string ROOT = "/";

        private const string PARENT_SEGMENT = "/..";

        /// <summary>
        /// The folder above <paramref name="path"/>. The root's parent is the root, and so is the parent of
        /// anything that does not name a folder.
        /// </summary>
        public static string Parent(string? path)
        {
            var current = TrimTrailingSeparator(path);
            if (string.IsNullOrEmpty(current) || current == ROOT)
                return ROOT;

            var lastSlash = current.LastIndexOf('/');
            if (lastSlash < 0)
                return ROOT;
            if (lastSlash == 0)
                return ROOT;

            return current.Substring(0, lastSlash);
        }

        /// <summary>
        /// The path a folder listing should actually be asked for: empty becomes the root, and a
        /// <c>/..</c> typed on the end is applied rather than sent to the server. A chain of them is
        /// applied in turn, so <c>/a/b/../..</c> is the root and not a listing request that most servers
        /// answer with "not exists".
        /// </summary>
        public static string Resolve(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ROOT;

            var current = path!.Trim();
            if (!current.EndsWith(PARENT_SEGMENT, StringComparison.Ordinal))
                return current;

            // Only a run of them on the end is applied. A ".." in the middle of a path is left alone: as
            // far as this side can tell it is a folder somebody named that, and the server is the one that
            // knows.
            var head = current;
            while (head.EndsWith(PARENT_SEGMENT, StringComparison.Ordinal))
                head = head.Substring(0, head.Length - PARENT_SEGMENT.Length);

            var levels = (current.Length - head.Length) / PARENT_SEGMENT.Length;
            var resolved = string.IsNullOrEmpty(head) ? ROOT : head;
            for (var i = 0; i < levels; i++)
                resolved = Parent(resolved);

            return resolved;
        }

        /// <summary>
        /// Drops one trailing <c>/</c>, because <c>/a/b/</c> and <c>/a/b</c> are the same folder and only
        /// one of them has a name to take off. The root keeps its slash: it is the whole path.
        /// </summary>
        private static string TrimTrailingSeparator(string? path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (path == ROOT) return ROOT;
            return path!.EndsWith("/", StringComparison.Ordinal) ? path.Substring(0, path.Length - 1) : path;
        }
    }
}
