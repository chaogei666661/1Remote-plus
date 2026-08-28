using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace _1RM.Service.Audit
{
    /// <summary>
    /// An append-only local record of every time a credential left the app.
    ///
    /// The connection log answers "who reached that host". It says nothing at all about the operator who
    /// exported the whole server list to a JSON file with every password in cleartext, or copied one to the
    /// clipboard, or packed the credential database into a backup and carried it away — which is the first
    /// question an insider-threat or leaver review asks, and the one this app could not answer.
    ///
    /// Same day files, same retention and the same folder as the connection log, under a prefix of its own
    /// so neither reader is ever handed a line of the other's shape.
    /// </summary>
    public sealed class SecretAccessLog : AuditLogBase<SecretAccessRecord>
    {
        public const string FILE_PREFIX = "secrets-";
        public const string FILE_EXTENSION = AuditDayFiles.FILE_EXTENSION;

        public SecretAccessLog() : base("1Rm.SecretAccessLog")
        {
        }

        protected override string FilePrefix => FILE_PREFIX;

        public static string DirectoryPath => AuditDayFiles.DirectoryPath;

        public static string FilePathFor(DateTime utcDay) => AuditDayFiles.FilePathFor(FILE_PREFIX, utcDay);

        /// <summary>Every record still on disk, oldest first. A line that does not parse is skipped.</summary>
        public static IReadOnlyList<SecretAccessRecord> ReadAll()
        {
            return AuditDayFiles.ReadAllLines(FILE_PREFIX)
                .Select(TryParse)
                .Where(x => x != null)
                .Select(x => x!)
                .OrderBy(x => x.TimeUtc)
                .ToList();
        }

        public static SecretAccessRecord? TryParse(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            try
            {
                return JsonConvert.DeserializeObject<SecretAccessRecord>(line!);
            }
            catch
            {
                return null;
            }
        }

        public static IReadOnlyList<string> DayFiles() => AuditDayFiles.DayFiles(FILE_PREFIX);

        public static DateTime? DayOf(string path) => AuditDayFiles.DayOf(FILE_PREFIX, path);

        public static int Prune(int retentionDays, DateTime utcNow) =>
            AuditDayFiles.Prune(FILE_PREFIX, retentionDays, utcNow);

        public static int Clear() => AuditDayFiles.Clear(FILE_PREFIX);

        /// <summary>
        /// The file the credential-access rows go to when the connection rows are going to
        /// <paramref name="connectionCsvPath"/>. A sibling rather than a second dialog: an auditor asked for
        /// "the log", and the two halves belong in the same folder with names that pair up.
        /// </summary>
        public static string SiblingCsvPath(string connectionCsvPath)
        {
            if (string.IsNullOrWhiteSpace(connectionCsvPath)) throw new ArgumentNullException(nameof(connectionCsvPath));
            var directory = Path.GetDirectoryName(connectionCsvPath) ?? "";
            var name = Path.GetFileNameWithoutExtension(connectionCsvPath);
            var extension = Path.GetExtension(connectionCsvPath);
            return Path.Combine(directory, name + "-secrets" + extension);
        }

        /// <summary>Writes every record to <paramref name="csvPath"/>. Returns how many rows were written.</summary>
        public static int ExportCsv(string csvPath)
        {
            var records = ReadAll();
            AppPathHelper.CreateDirIfNotExist(csvPath, true);
            // A BOM, because Excel on a non-UTF-8 locale otherwise mangles every non-ASCII server name.
            File.WriteAllText(csvPath, SecretAccessCsv.Write(records), new UTF8Encoding(true));
            return records.Count;
        }
    }
}
