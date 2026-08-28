using System;
using System.Collections.Concurrent;
using System.Threading;
using Newtonsoft.Json;
using Shawn.Utils;

namespace _1RM.Service.Audit
{
    /// <summary>
    /// The writing half of an audit log: a queue, one background thread, and an append per record.
    ///
    /// Writes go through that one thread because the actions being recorded — opening a session, copying a
    /// password — are things a user is waiting on, and an audit line must never be the reason one of them
    /// is slow. Reading, retention and export are static on each subclass, because they are done from the
    /// settings page with no instance in hand.
    /// </summary>
    public abstract class AuditLogBase<T> : IDisposable where T : class, IAuditRecord
    {
        private readonly BlockingCollection<T> _queue =
            new BlockingCollection<T>(new ConcurrentQueue<T>(), 4096);

        private readonly Thread _writer;
        private int _disposed;

        protected AuditLogBase(string threadName)
        {
            _writer = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = threadName,
            };
            _writer.Start();
        }

        /// <summary>The day-file name prefix this log's records are written under.</summary>
        protected abstract string FilePrefix { get; }

        /// <summary>Whether records are written at all. Owned by the settings page.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Queues a record. Returns immediately; the write happens on the audit thread.
        /// </summary>
        public void Record(T? record)
        {
            if (record == null) return;
            if (!Enabled) return;
            if (Volatile.Read(ref _disposed) != 0) return;

            if (string.IsNullOrEmpty(record.LocalUser))
                record.LocalUser = SafeEnvironment(() => Environment.UserName);
            if (string.IsNullOrEmpty(record.LocalMachine))
                record.LocalMachine = SafeEnvironment(() => Environment.MachineName);

            // Dropping is the right failure here. The queue only fills if the disk has stopped accepting
            // writes, and blocking the caller on that would turn a logging problem into an outage.
            if (!_queue.TryAdd(record))
                SimpleLogHelper.Warning($"{GetType().Name}: the audit queue is full, a record was dropped");
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
                    SimpleLogHelper.Warning($"{GetType().Name}: could not write a record, {e.Message}");
                }
            }
        }

        /// <summary>
        /// Writes one record synchronously. Public for the tests, which must not race a background thread;
        /// the app goes through <see cref="Record"/>.
        /// </summary>
        public void AppendNow(T record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            AuditDayFiles.Append(FilePrefix, record.TimeUtc, JsonConvert.SerializeObject(record, Formatting.None));
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
