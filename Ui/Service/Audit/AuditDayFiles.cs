using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Shawn.Utils;

namespace _1RM.Service.Audit
{
    /// <summary>
    /// The day files the audit logs are made of, and everything that can be done to them without knowing
    /// what a record contains.
    ///
    /// Format is JSON Lines, one file per UTC day, one prefix per kind of record. Line-oriented because an
    /// append is then a single write that cannot corrupt what is already there — a JSON array would have to
    /// be rewritten whole on every append, and a crash mid-rewrite would take the day's history with it.
    /// Per day because that makes retention a file delete rather than a rewrite, and because grep and
    /// Get-Content work on it as is. Per prefix because a reader deserialising a day file must not be handed
    /// a line of a shape it does not expect: two kinds of record in one file would each parse as a
    /// half-empty instance of the other.
    /// </summary>
    public static class AuditDayFiles
    {
        public const string FILE_EXTENSION = ".jsonl";
        private const string FILE_DATE_FORMAT = "yyyy-MM-dd";

        /// <summary>
        /// One lock for the whole folder rather than one per log. Appends are short, there are two writers
        /// in the process, and a lock per prefix would buy nothing but a way to get the pairing wrong.
        /// </summary>
        private static readonly object FileLock = new object();

        /// <summary>
        /// Where the day files live. Under locality rather than next to the profile: an audit trail is about
        /// what happened on this machine, so it must not travel to another one through a synced data source.
        /// </summary>
        public static string DirectoryPath => Path.Combine(AppPathHelper.Instance.LocalityDirPath, "audit");

        public static string FilePathFor(string prefix, DateTime utcDay) =>
            Path.Combine(DirectoryPath, prefix + utcDay.ToString(FILE_DATE_FORMAT, CultureInfo.InvariantCulture) + FILE_EXTENSION);

        /// <summary>Appends one line to the day file <paramref name="utcDay"/> belongs to.</summary>
        public static void Append(string prefix, DateTime utcDay, string line)
        {
            // A record must never span lines, or one bad entry would swallow the next when reading back.
            line = line.Replace("\r", "").Replace("\n", "");

            lock (FileLock)
            {
                AppPathHelper.CreateDirIfNotExist(DirectoryPath, false);
                var path = FilePathFor(prefix, utcDay);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.WriteLine(line);
            }
        }

        /// <summary>Day files of one kind in chronological order, by the date in the name rather than by mtime.</summary>
        public static IReadOnlyList<string> DayFiles(string prefix)
        {
            try
            {
                if (!Directory.Exists(DirectoryPath)) return Array.Empty<string>();
                return Directory.GetFiles(DirectoryPath, prefix + "*" + FILE_EXTENSION)
                    .Where(x => DayOf(prefix, x) != null)
                    .OrderBy(x => DayOf(prefix, x)!.Value)
                    .ToList();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"AuditDayFiles: cannot list {DirectoryPath}, {e.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>The UTC day a file name stands for, or null when the name is not one of ours.</summary>
        public static DateTime? DayOf(string prefix, string path)
        {
            var name = Path.GetFileNameWithoutExtension(path) ?? "";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            var stamp = name.Substring(prefix.Length);
            return DateTime.TryParseExact(stamp, FILE_DATE_FORMAT, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day)
                ? day
                : (DateTime?)null;
        }

        /// <summary>
        /// Every line of every day file of one kind, oldest file first. A file that cannot be read is
        /// skipped rather than aborting the walk: a log truncated by a power cut should still yield the rest
        /// of the month.
        /// </summary>
        public static IEnumerable<string> ReadAllLines(string prefix)
        {
            foreach (var path in DayFiles(prefix))
            {
                foreach (var line in ReadLines(path))
                    yield return line;
            }
        }

        private static IEnumerable<string> ReadLines(string path)
        {
            try
            {
                return File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"AuditDayFiles: cannot read {path}, {e.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Deletes day files older than <paramref name="retentionDays"/>. Zero or less keeps everything —
        /// an organisation that has to keep records for a year should not lose them to a default.
        /// Returns how many files were deleted.
        /// </summary>
        public static int Prune(string prefix, int retentionDays, DateTime utcNow)
        {
            if (retentionDays <= 0) return 0;
            var cutoff = utcNow.Date.AddDays(-retentionDays);
            var deleted = 0;
            foreach (var path in DayFiles(prefix))
            {
                var day = DayOf(prefix, path);
                if (day == null || day.Value >= cutoff) continue;
                if (TryDelete(path)) ++deleted;
            }
            if (deleted > 0)
                SimpleLogHelper.Info($"AuditDayFiles: pruned {deleted} '{prefix}' day file(s) older than {retentionDays} days");
            return deleted;
        }

        /// <summary>Removes every day file of one kind. Returns how many went.</summary>
        public static int Clear(string prefix)
        {
            var deleted = 0;
            foreach (var path in DayFiles(prefix))
            {
                if (TryDelete(path)) ++deleted;
            }
            return deleted;
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
                SimpleLogHelper.Warning($"AuditDayFiles: cannot delete {path}, {e.Message}");
                return false;
            }
        }
    }
}
