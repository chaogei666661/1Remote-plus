using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Shawn.Utils;

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
    /// Format is JSON Lines, one file per UTC day. Line-oriented because an append is then a single write
    /// that cannot corrupt what is already there — a JSON array would have to be rewritten whole on every
    /// connect, and a crash mid-rewrite would take the day's history with it. Per day because that makes
    /// retention a file delete rather than a rewrite, and because grep and Get-Content work on it as is.
    ///
    /// Writes go through one background thread. A connect already touches the disk more than it should, and
    /// an audit line must never be the reason a session is slow to open or the UI stutters.
    /// </summary>
    public sealed class ConnectionAuditLog : IDisposable
    {
        public const string FILE_PREFIX = "connections-";
        public const string FILE_EXTENSION = ".jsonl";
        private const string FILE_DATE_FORMAT = "yyyy-MM-dd";

        private readonly BlockingCollection<ConnectionAuditRecord> _queue =
            new BlockingCollection<ConnectionAuditRecord>(new ConcurrentQueue<ConnectionAuditRecord>(), 4096);

        private readonly Thread _writer;
        private readonly object _fileLock = new object();
        private int _disposed;

        public ConnectionAuditLog()
        {
            _writer = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "1Rm.AuditLog",
            };
            _writer.Start();
        }

        /// <summary>Whether records are written at all. Owned by the settings page.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Where the day files live. Under locality rather than next to the profile: an audit trail is about
        /// what happened on this machine, so it must not travel to another one through a synced data source.
        /// </summary>
        public static string DirectoryPath => Path.Combine(AppPathHelper.Instance.LocalityDirPath, "audit");

        public static string FilePathFor(DateTime utcDay) =>
            Path.Combine(DirectoryPath, FILE_PREFIX + utcDay.ToString(FILE_DATE_FORMAT, CultureInfo.InvariantCulture) + FILE_EXTENSION);

        /// <summary>
        /// Queues a record. Returns immediately; the write happens on the audit thread.
        /// </summary>
        public void Record(ConnectionAuditRecord record)
        {
            if (record == null) return;
            if (!Enabled) return;
            if (Volatile.Read(ref _disposed) != 0) return;

            if (string.IsNullOrEmpty(record.LocalUser))
                record.LocalUser = SafeEnvironment(() => Environment.UserName);
            if (string.IsNullOrEmpty(record.LocalMachine))
                record.LocalMachine = SafeEnvironment(() => Environment.MachineName);

            // Dropping is the right failure here. The queue only fills if the disk has stopped accepting
            // writes, and blocking a connect on that would turn a logging problem into an outage.
            if (!_queue.TryAdd(record))
                SimpleLogHelper.Warning("ConnectionAuditLog: the audit queue is full, a record was dropped");
        }

        private static string SafeEnvironment(Func<string> read)
        {
            try
            {
                return read() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private void WriterLoop()
        {
            foreach (var record in _queue.GetConsumingEnumerable())
            {
                try
                {
                    AppendNow(record);
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Warning($"ConnectionAuditLog: could not write a record, {e.Message}");
                }
            }
        }

        /// <summary>
        /// Writes one record synchronously. Public for the tests, which must not race a background thread;
        /// the app goes through <see cref="Record"/>.
        /// </summary>
        public void AppendNow(ConnectionAuditRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var line = JsonConvert.SerializeObject(record, Formatting.None);
            // A record must never span lines, or one bad entry would swallow the next when reading back.
            line = line.Replace("\r", "").Replace("\n", "");

            lock (_fileLock)
            {
                AppPathHelper.CreateDirIfNotExist(DirectoryPath, false);
                var path = FilePathFor(record.TimeUtc);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.WriteLine(line);
            }
        }

        /// <summary>
        /// Every record still on disk, oldest first. A line that does not parse is skipped rather than
        /// aborting the read: a log truncated by a power cut should still yield the rest of the month.
        /// </summary>
        public static IReadOnlyList<ConnectionAuditRecord> ReadAll()
        {
            var result = new List<ConnectionAuditRecord>();
            foreach (var path in DayFiles())
            {
                foreach (var line in ReadLines(path))
                {
                    var record = TryParse(line);
                    if (record != null)
                        result.Add(record);
                }
            }
            return result.OrderBy(x => x.TimeUtc).ToList();
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

        private static IEnumerable<string> ReadLines(string path)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"ConnectionAuditLog: cannot read {path}, {e.Message}");
                return Array.Empty<string>();
            }
            return lines;
        }

        /// <summary>Day files in chronological order, by the date in the name rather than by mtime.</summary>
        public static IReadOnlyList<string> DayFiles()
        {
            try
            {
                if (!Directory.Exists(DirectoryPath)) return Array.Empty<string>();
                return Directory.GetFiles(DirectoryPath, FILE_PREFIX + "*" + FILE_EXTENSION)
                    .Where(x => DayOf(x) != null)
                    .OrderBy(x => DayOf(x)!.Value)
                    .ToList();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"ConnectionAuditLog: cannot list {DirectoryPath}, {e.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>The UTC day a file name stands for, or null when the name is not one of ours.</summary>
        public static DateTime? DayOf(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path) ?? "";
            if (!name.StartsWith(FILE_PREFIX, StringComparison.OrdinalIgnoreCase)) return null;
            var stamp = name.Substring(FILE_PREFIX.Length);
            return DateTime.TryParseExact(stamp, FILE_DATE_FORMAT, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day)
                ? day
                : (DateTime?)null;
        }

        /// <summary>
        /// Deletes day files older than <paramref name="retentionDays"/>. Zero or less keeps everything —
        /// an organisation that has to keep records for a year should not lose them to a default.
        /// Returns how many files were deleted.
        /// </summary>
        public static int Prune(int retentionDays, DateTime utcNow)
        {
            if (retentionDays <= 0) return 0;
            var cutoff = utcNow.Date.AddDays(-retentionDays);
            var deleted = 0;
            foreach (var path in DayFiles())
            {
                var day = DayOf(path);
                if (day == null || day.Value >= cutoff) continue;
                try
                {
                    File.Delete(path);
                    ++deleted;
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Warning($"ConnectionAuditLog: cannot delete {path}, {e.Message}");
                }
            }
            if (deleted > 0)
                SimpleLogHelper.Info($"ConnectionAuditLog: pruned {deleted} day file(s) older than {retentionDays} days");
            return deleted;
        }

        /// <summary>Removes every day file. Returns how many went.</summary>
        public static int Clear()
        {
            var deleted = 0;
            foreach (var path in DayFiles())
            {
                try
                {
                    File.Delete(path);
                    ++deleted;
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Warning($"ConnectionAuditLog: cannot delete {path}, {e.Message}");
                }
            }
            return deleted;
        }

        /// <summary>Writes every record to <paramref name="csvPath"/>. Returns how many rows were written.</summary>
        public static int ExportCsv(string csvPath)
        {
            var records = ReadAll();
            AppPathHelper.CreateDirIfNotExist(csvPath, true);
            // A BOM, because Excel on a non-UTF-8 locale otherwise mangles every non-ASCII server name.
            File.WriteAllText(csvPath, AuditCsv.Write(records), new UTF8Encoding(true));
            return records.Count;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _queue.CompleteAdding();
            // Bounded: shutdown must not hang on a disk that stopped answering, and the watchdog would
            // otherwise pull the plug on the whole process for the sake of one log line.
            _writer.Join(TimeSpan.FromSeconds(2));
            _queue.Dispose();
        }
    }
}
