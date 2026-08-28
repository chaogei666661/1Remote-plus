using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace _1RM.Service.Audit
{
    /// <summary>
    /// An append-only local record of every connection attempt.
    ///
    /// Every comparable product keeps one — Devolutions RDM, Royal TS and Keeper Connection Manager all do,
    /// and it is the first thing asked for after an incident: who reached that host, from which machine,
    /// when, and did it succeed. 1Remote kept only a last-connect timestamp per server, for sorting the
    /// list, which cannot answer any of that.
    ///
    /// The file mechanics are in <see cref="AuditDayFiles"/> and the writer thread in
    /// <see cref="AuditLogBase{T}"/>, both shared with <see cref="SecretAccessLog"/>.
    /// </summary>
    public sealed class ConnectionAuditLog : AuditLogBase<ConnectionAuditRecord>
    {
        public const string FILE_PREFIX = "connections-";
        public const string FILE_EXTENSION = AuditDayFiles.FILE_EXTENSION;

        public ConnectionAuditLog() : base("1Rm.AuditLog")
        {
        }

        protected override string FilePrefix => FILE_PREFIX;

        public static string DirectoryPath => AuditDayFiles.DirectoryPath;

        public static string FilePathFor(DateTime utcDay) => AuditDayFiles.FilePathFor(FILE_PREFIX, utcDay);

        /// <summary>
        /// Every record still on disk, oldest first. A line that does not parse is skipped rather than
        /// aborting the read: a log truncated by a power cut should still yield the rest of the month.
        /// </summary>
        public static IReadOnlyList<ConnectionAuditRecord> ReadAll()
        {
            return AuditDayFiles.ReadAllLines(FILE_PREFIX)
                .Select(TryParse)
                .Where(x => x != null)
                .Select(x => x!)
                .OrderBy(x => x.TimeUtc)
                .ToList();
        }

        public static ConnectionAuditRecord? TryParse(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            try
            {
                return JsonConvert.DeserializeObject<ConnectionAuditRecord>(line!);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Day files in chronological order, by the date in the name rather than by mtime.</summary>
        public static IReadOnlyList<string> DayFiles() => AuditDayFiles.DayFiles(FILE_PREFIX);

        /// <summary>The UTC day a file name stands for, or null when the name is not one of ours.</summary>
        public static DateTime? DayOf(string path) => AuditDayFiles.DayOf(FILE_PREFIX, path);

        /// <summary>
        /// Deletes day files older than <paramref name="retentionDays"/>. Zero or less keeps everything.
        /// Returns how many files were deleted.
        /// </summary>
        public static int Prune(int retentionDays, DateTime utcNow) =>
            AuditDayFiles.Prune(FILE_PREFIX, retentionDays, utcNow);

        /// <summary>Removes every day file. Returns how many went.</summary>
        public static int Clear() => AuditDayFiles.Clear(FILE_PREFIX);

        /// <summary>Writes every record to <paramref name="csvPath"/>. Returns how many rows were written.</summary>
        public static int ExportCsv(string csvPath)
        {
            var records = ReadAll();
            AppPathHelper.CreateDirIfNotExist(csvPath, true);
            // A BOM, because Excel on a non-UTF-8 locale otherwise mangles every non-ASCII server name.
            File.WriteAllText(csvPath, AuditCsv.Write(records), new UTF8Encoding(true));
            return records.Count;
        }
    }
}
