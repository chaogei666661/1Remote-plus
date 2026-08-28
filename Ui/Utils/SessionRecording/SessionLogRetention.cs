using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shawn.Utils;

namespace _1RM.Utils.SessionRecording
{
    /// <summary>
    /// Keeps the terminal-recording folder inside the limits the user set.
    ///
    /// Recording was added without one. A session log holds everything the terminal printed — command
    /// history, file contents, occasionally a secret someone echoed — so a folder of them that only ever
    /// grows is a disclosure problem long before it is a disk problem. Every comparable product treats
    /// recording and retention as one feature; this is the missing half.
    ///
    /// Two independent limits, both optional. Age is what a policy is usually written in ("we keep 30
    /// days"); total size is what stops one very long session from filling the disk before the age limit
    /// ever bites.
    /// </summary>
    public static class SessionLogRetention
    {
        public const string LOG_SEARCH_PATTERN = "*.log";

        /// <summary>
        /// Deletes recordings older than <paramref name="maxAgeDays"/>, then the oldest remaining ones until
        /// the folder is under <paramref name="maxMegabytes"/>. Either limit is off when it is 0 or less.
        /// Returns how many files were deleted.
        /// </summary>
        /// <param name="now">Injected so the age rule is testable without waiting.</param>
        public static int Prune(string folder, int maxAgeDays, int maxMegabytes, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(folder)) return 0;
            if (maxAgeDays <= 0 && maxMegabytes <= 0) return 0;

            var files = Enumerate(folder);
            if (files.Count == 0) return 0;

            var deleted = 0;

            if (maxAgeDays > 0)
            {
                var cutoff = now.AddDays(-maxAgeDays);
                foreach (var file in files.Where(x => x.LastWriteTime < cutoff).ToArray())
                {
                    if (!TryDelete(file.FullName)) continue;
                    files.Remove(file);
                    ++deleted;
                }
            }

            if (maxMegabytes > 0)
            {
                var budget = (long)maxMegabytes * 1024 * 1024;
                var total = files.Sum(x => x.Length);
                // Oldest first: a recording being written right now is the last thing to throw away.
                foreach (var file in files.OrderBy(x => x.LastWriteTime).ToArray())
                {
                    if (total <= budget) break;
                    if (!TryDelete(file.FullName)) continue;
                    total -= file.Length;
                    ++deleted;
                }
            }

            if (deleted > 0)
                SimpleLogHelper.Info($"SessionLogRetention: deleted {deleted} recording(s) from {folder}");
            return deleted;
        }

        /// <summary>Total bytes the recordings in <paramref name="folder"/> occupy.</summary>
        public static long TotalBytes(string folder) => Enumerate(folder).Sum(x => x.Length);

        private static List<FileInfo> Enumerate(string folder)
        {
            try
            {
                var dir = new DirectoryInfo(folder);
                if (!dir.Exists) return new List<FileInfo>();
                return dir.GetFiles(LOG_SEARCH_PATTERN, SearchOption.TopDirectoryOnly).ToList();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"SessionLogRetention: cannot list {folder}, {e.Message}");
                return new List<FileInfo>();
            }
        }

        private static bool TryDelete(string path)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception e)
            {
                // A recording that PuTTY still has open is the normal case here, not an error.
                SimpleLogHelper.Debug($"SessionLogRetention: cannot delete {path}, {e.Message}");
                return false;
            }
        }
    }
}
