using System;
using System.IO;
using System.Linq;
using System.Text;
using _1RM.Service;
using _1RM.Service.Audit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Service.Audit
{
    /// <summary>
    /// The audit log is what an access review is built from, so the cases below are about it being complete
    /// and readable rather than about it being fast: a truncated day file must not cost the rest of the
    /// month, retention must not delete more than it was asked to, and nothing secret may reach the file.
    /// </summary>
    [TestClass]
    public class ConnectionAuditLogTests
    {
        private string _root = "";
        private AppPathHelper _originalPaths = AppPathHelper.Instance;

        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
            _originalPaths = AppPathHelper.Instance;
            _root = Path.Combine(Path.GetTempPath(), $"1rm-audit-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);
            AppPathHelper.Instance = new AppPathHelper(_root, _root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            AppPathHelper.Instance = _originalPaths;
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
            }
            catch (Exception)
            {
                // a leftover temp folder is not worth failing a test over
            }
        }

        private static ConnectionAuditRecord Rec(DateTime utc, EAuditEvent e = EAuditEvent.ConnectStarted, string name = "web01")
        {
            return new ConnectionAuditRecord
            {
                TimeUtc = utc,
                Event = e,
                Protocol = "SSH",
                ServerId = "id-" + name,
                ServerName = name,
                Address = "10.0.0.5",
                Port = 22,
                RemoteUser = "deploy",
                DataSource = "Local",
                ConnectionId = "id-" + name,
                LocalUser = "alice",
                LocalMachine = "LAPTOP",
            };
        }

        [TestMethod]
        public void ARecordSurvivesARoundTrip()
        {
            using var log = new ConnectionAuditLog();
            var written = Rec(new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc), EAuditEvent.SessionOpened);
            log.AppendNow(written);

            var all = ConnectionAuditLog.ReadAll();
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual(EAuditEvent.SessionOpened, all[0].Event);
            Assert.AreEqual("web01", all[0].ServerName);
            Assert.AreEqual("deploy", all[0].RemoteUser);
            Assert.AreEqual(22, all[0].Port);
            Assert.AreEqual(DateTimeKind.Utc, all[0].TimeUtc.ToUniversalTime().Kind);
        }

        [TestMethod]
        public void RecordsLandInTheDayFileTheirTimestampBelongsTo()
        {
            using var log = new ConnectionAuditLog();
            log.AppendNow(Rec(new DateTime(2026, 5, 1, 23, 0, 0, DateTimeKind.Utc)));
            log.AppendNow(Rec(new DateTime(2026, 5, 2, 1, 0, 0, DateTimeKind.Utc)));

            var files = ConnectionAuditLog.DayFiles();
            Assert.AreEqual(2, files.Count);
            StringAssert.Contains(Path.GetFileName(files[0]), "2026-05-01");
            StringAssert.Contains(Path.GetFileName(files[1]), "2026-05-02");
        }

        [TestMethod]
        public void ReadAllIsSortedAcrossDayFiles()
        {
            using var log = new ConnectionAuditLog();
            log.AppendNow(Rec(new DateTime(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc), name: "third"));
            log.AppendNow(Rec(new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc), name: "first"));
            log.AppendNow(Rec(new DateTime(2026, 5, 2, 9, 0, 0, DateTimeKind.Utc), name: "second"));

            var all = ConnectionAuditLog.ReadAll();
            CollectionAssert.AreEqual(new[] { "first", "second", "third" }, all.Select(x => x.ServerName).ToArray());
        }

        [TestMethod]
        public void ATruncatedLineDoesNotCostTheRestOfTheFile()
        {
            using var log = new ConnectionAuditLog();
            var day = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
            log.AppendNow(Rec(day, name: "good1"));

            // What a power cut mid-write leaves behind.
            File.AppendAllText(ConnectionAuditLog.FilePathFor(day), "{\"t\":\"2026-05-0" + Environment.NewLine, Encoding.UTF8);
            log.AppendNow(Rec(day.AddHours(1), name: "good2"));

            var all = ConnectionAuditLog.ReadAll();
            CollectionAssert.AreEqual(new[] { "good1", "good2" }, all.Select(x => x.ServerName).ToArray());
        }

        [TestMethod]
        public void ANewlineInAServerNameCannotForgeAnExtraRecord()
        {
            using var log = new ConnectionAuditLog();
            var day = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
            var record = Rec(day);
            record.ServerName = "web01\n{\"e\":0,\"name\":\"forged\"}";
            log.AppendNow(record);

            var all = ConnectionAuditLog.ReadAll();
            Assert.AreEqual(1, all.Count, "one Append must produce exactly one record");
            Assert.IsFalse(all.Any(x => x.ServerName == "forged"));
        }

        [TestMethod]
        public void RetentionDeletesOnlyWhatIsPastTheCutoff()
        {
            using var log = new ConnectionAuditLog();
            var now = new DateTime(2026, 5, 30, 12, 0, 0, DateTimeKind.Utc);
            log.AppendNow(Rec(now.AddDays(-40), name: "old"));
            log.AppendNow(Rec(now.AddDays(-10), name: "recent"));
            log.AppendNow(Rec(now, name: "today"));

            Assert.AreEqual(1, ConnectionAuditLog.Prune(30, now));

            var all = ConnectionAuditLog.ReadAll();
            CollectionAssert.AreEqual(new[] { "recent", "today" }, all.Select(x => x.ServerName).ToArray());
        }

        [TestMethod]
        public void RetentionOfZeroKeepsEverything()
        {
            using var log = new ConnectionAuditLog();
            var now = new DateTime(2026, 5, 30, 12, 0, 0, DateTimeKind.Utc);
            log.AppendNow(Rec(now.AddDays(-400), name: "ancient"));

            Assert.AreEqual(0, ConnectionAuditLog.Prune(0, now));
            Assert.AreEqual(1, ConnectionAuditLog.ReadAll().Count);
        }

        [TestMethod]
        public void PruningIgnoresFilesThatAreNotOurs()
        {
            using var log = new ConnectionAuditLog();
            var now = new DateTime(2026, 5, 30, 12, 0, 0, DateTimeKind.Utc);
            log.AppendNow(Rec(now, name: "today"));

            var stranger = Path.Combine(ConnectionAuditLog.DirectoryPath, "notes.txt");
            File.WriteAllText(stranger, "keep me");
            var oddName = Path.Combine(ConnectionAuditLog.DirectoryPath, "connections-not-a-date.jsonl");
            File.WriteAllText(oddName, "keep me too");

            ConnectionAuditLog.Prune(1, now.AddDays(500));

            Assert.IsTrue(File.Exists(stranger));
            Assert.IsTrue(File.Exists(oddName));
        }

        [TestMethod]
        public void DisablingItStopsRecords()
        {
            using var log = new ConnectionAuditLog { Enabled = false };
            log.Record(Rec(DateTime.UtcNow));
            log.Dispose();
            Assert.AreEqual(0, ConnectionAuditLog.ReadAll().Count);
        }

        [TestMethod]
        public void QueuedRecordsAreFlushedByDispose()
        {
            var log = new ConnectionAuditLog();
            log.Record(Rec(new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc), name: "queued"));
            log.Dispose();

            var all = ConnectionAuditLog.ReadAll();
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual("queued", all[0].ServerName);
        }

        [TestMethod]
        public void RecordFillsInWhoWasAtTheKeyboard()
        {
            var log = new ConnectionAuditLog();
            var record = Rec(new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc));
            record.LocalUser = "";
            record.LocalMachine = "";
            log.Record(record);
            log.Dispose();

            var all = ConnectionAuditLog.ReadAll();
            Assert.AreEqual(1, all.Count);
            Assert.AreNotEqual("", all[0].LocalUser);
            Assert.AreNotEqual("", all[0].LocalMachine);
        }

        [TestMethod]
        public void ExportWritesEveryRecordAsCsv()
        {
            using var log = new ConnectionAuditLog();
            log.AppendNow(Rec(new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc), name: "a"));
            log.AppendNow(Rec(new DateTime(2026, 5, 2, 9, 0, 0, DateTimeKind.Utc), name: "b"));

            var csvPath = Path.Combine(_root, "export", "audit.csv");
            Assert.AreEqual(2, ConnectionAuditLog.ExportCsv(csvPath));

            var text = File.ReadAllText(csvPath);
            StringAssert.StartsWith(text.TrimStart('\uFEFF'), AuditCsv.Header);
            StringAssert.Contains(text, ",a,");
            StringAssert.Contains(text, ",b,");
        }

        [TestMethod]
        public void ClearRemovesEveryDayFile()
        {
            using var log = new ConnectionAuditLog();
            log.AppendNow(Rec(new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc)));
            log.AppendNow(Rec(new DateTime(2026, 5, 2, 9, 0, 0, DateTimeKind.Utc)));

            Assert.AreEqual(2, ConnectionAuditLog.Clear());
            Assert.AreEqual(0, ConnectionAuditLog.ReadAll().Count);
        }

        [TestMethod]
        public void ReadingAnAbsentFolderIsNotAnError()
        {
            Assert.AreEqual(0, ConnectionAuditLog.ReadAll().Count);
            Assert.AreEqual(0, ConnectionAuditLog.DayFiles().Count);
            Assert.AreEqual(0, ConnectionAuditLog.Prune(30, DateTime.UtcNow));
        }

        [TestMethod]
        public void TheAuditFolderIsUnderLocalityNotNextToTheProfile()
        {
            // Locality does not travel with a shared or synced data source; an audit trail is about this
            // machine and must not be merged with another one's.
            StringAssert.Contains(ConnectionAuditLog.DirectoryPath, AppPathHelper.Instance.LocalityDirPath);
        }
    }
}
