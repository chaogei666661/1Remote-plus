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
    /// The credential-access log: who took a password, a server list or a backup out of the app, and where
    /// it went. The connection log could not answer that, because none of those events involve a connection.
    ///
    /// The cases that matter most are the ones about the two logs staying apart. They share a folder, a
    /// retention setting and a "delete the audit log" button, and a reader handed a line of the wrong shape
    /// would silently produce a half-empty record of the right one rather than an error.
    /// </summary>
    [TestClass]
    public class SecretAccessLogTests
    {
        private string _root = "";
        private AppPathHelper _originalPaths = AppPathHelper.Instance;

        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
            _originalPaths = AppPathHelper.Instance;
            _root = Path.Combine(Path.GetTempPath(), $"1rm-secret-audit-{Guid.NewGuid():N}");
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

        private static SecretAccessRecord Rec(DateTime utc,
            ESecretAccessEvent e = ESecretAccessEvent.PasswordCopied,
            string name = "web01",
            string destination = SecretAccessRecord.DESTINATION_CLIPBOARD)
        {
            return new SecretAccessRecord
            {
                TimeUtc = utc,
                Event = e,
                ServerId = "id-" + name,
                ServerName = name,
                Protocol = "SSH",
                Address = "10.0.0.5",
                RemoteUser = "deploy",
                DataSource = "Local",
                Destination = destination,
                LocalUser = "alice",
                LocalMachine = "LAPTOP",
            };
        }

        [TestMethod]
        public void ARecordSurvivesARoundTrip()
        {
            using var log = new SecretAccessLog();
            log.AppendNow(Rec(new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc)));

            var all = SecretAccessLog.ReadAll();
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual(ESecretAccessEvent.PasswordCopied, all[0].Event);
            Assert.AreEqual("web01", all[0].ServerName);
            Assert.AreEqual("deploy", all[0].RemoteUser);
            Assert.AreEqual(SecretAccessRecord.DESTINATION_CLIPBOARD, all[0].Destination);
            Assert.AreEqual(1, all[0].Count);
        }

        [TestMethod]
        public void AnExportOfTheWholeListIsRecordedWithItsCountAndWhereItWent()
        {
            using var log = new SecretAccessLog();
            var record = Rec(new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
                ESecretAccessEvent.ServerListExported, name: "", destination: @"D:\share\everything.json");
            record.ServerId = "";
            record.Count = 47;
            log.AppendNow(record);

            var all = SecretAccessLog.ReadAll();
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual(47, all[0].Count);
            Assert.AreEqual(@"D:\share\everything.json", all[0].Destination);
        }

        [TestMethod]
        public void RecordsLandInTheDayFileTheirTimestampBelongsTo()
        {
            using var log = new SecretAccessLog();
            log.AppendNow(Rec(new DateTime(2026, 5, 1, 23, 0, 0, DateTimeKind.Utc)));
            log.AppendNow(Rec(new DateTime(2026, 5, 2, 1, 0, 0, DateTimeKind.Utc)));

            var files = SecretAccessLog.DayFiles();
            Assert.AreEqual(2, files.Count);
            StringAssert.Contains(Path.GetFileName(files[0]), "2026-05-01");
            StringAssert.Contains(Path.GetFileName(files[1]), "2026-05-02");
        }

        [TestMethod]
        public void ReadAllIsSortedAcrossDayFiles()
        {
            using var log = new SecretAccessLog();
            log.AppendNow(Rec(new DateTime(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc), name: "third"));
            log.AppendNow(Rec(new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc), name: "first"));
            log.AppendNow(Rec(new DateTime(2026, 5, 2, 9, 0, 0, DateTimeKind.Utc), name: "second"));

            CollectionAssert.AreEqual(new[] { "first", "second", "third" },
                SecretAccessLog.ReadAll().Select(x => x.ServerName).ToArray());
        }

        /// <summary>
        /// The reason the two logs have separate prefixes. Newtonsoft ignores fields it does not know and
        /// defaults the ones it does not find, so a credential record read as a connection record would come
        /// back as a ConnectStarted to port 0 — a fabricated connection in an access report.
        /// </summary>
        [TestMethod]
        public void TheConnectionLogNeverReadsACredentialRecord()
        {
            using var secrets = new SecretAccessLog();
            secrets.AppendNow(Rec(new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc)));

            Assert.AreEqual(0, ConnectionAuditLog.ReadAll().Count);
            Assert.AreEqual(0, ConnectionAuditLog.DayFiles().Count);
        }

        [TestMethod]
        public void TheCredentialLogNeverReadsAConnectionRecord()
        {
            using var connections = new ConnectionAuditLog();
            connections.AppendNow(new ConnectionAuditRecord
            {
                TimeUtc = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc),
                Event = EAuditEvent.SessionOpened,
                ServerName = "web01",
                LocalUser = "alice",
                LocalMachine = "LAPTOP",
            });

            Assert.AreEqual(0, SecretAccessLog.ReadAll().Count);
            Assert.AreEqual(0, SecretAccessLog.DayFiles().Count);
        }

        [TestMethod]
        public void BothLogsShareTheOneAuditFolder()
        {
            Assert.AreEqual(ConnectionAuditLog.DirectoryPath, SecretAccessLog.DirectoryPath);
            StringAssert.Contains(SecretAccessLog.DirectoryPath, AppPathHelper.Instance.LocalityDirPath);
        }

        [TestMethod]
        public void ATruncatedLineDoesNotCostTheRestOfTheFile()
        {
            using var log = new SecretAccessLog();
            var day = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
            log.AppendNow(Rec(day, name: "good1"));

            // What a power cut mid-write leaves behind.
            File.AppendAllText(SecretAccessLog.FilePathFor(day), "{\"t\":\"2026-05-0" + Environment.NewLine, Encoding.UTF8);
            log.AppendNow(Rec(day.AddHours(1), name: "good2"));

            CollectionAssert.AreEqual(new[] { "good1", "good2" },
                SecretAccessLog.ReadAll().Select(x => x.ServerName).ToArray());
        }

        [TestMethod]
        public void ANewlineInADestinationCannotForgeAnExtraRecord()
        {
            using var log = new SecretAccessLog();
            var record = Rec(new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc));
            record.Destination = "C:\\ok.json\n{\"e\":0,\"name\":\"forged\"}";
            log.AppendNow(record);

            var all = SecretAccessLog.ReadAll();
            Assert.AreEqual(1, all.Count, "one Append must produce exactly one record");
            Assert.IsFalse(all.Any(x => x.ServerName == "forged"));
        }

        [TestMethod]
        public void RetentionDeletesOnlyWhatIsPastTheCutoff()
        {
            using var log = new SecretAccessLog();
            var now = new DateTime(2026, 5, 30, 12, 0, 0, DateTimeKind.Utc);
            log.AppendNow(Rec(now.AddDays(-40), name: "old"));
            log.AppendNow(Rec(now.AddDays(-10), name: "recent"));

            Assert.AreEqual(1, SecretAccessLog.Prune(30, now));
            CollectionAssert.AreEqual(new[] { "recent" },
                SecretAccessLog.ReadAll().Select(x => x.ServerName).ToArray());
        }

        /// <summary>
        /// Pruning one log must not take the other with it: they sit in the same folder and differ only by
        /// the prefix of the file name.
        /// </summary>
        [TestMethod]
        public void PruningOneLogLeavesTheOtherAlone()
        {
            var day = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
            using (var secrets = new SecretAccessLog())
                secrets.AppendNow(Rec(day));
            using (var connections = new ConnectionAuditLog())
                connections.AppendNow(new ConnectionAuditRecord { TimeUtc = day, ServerName = "web01", LocalUser = "a", LocalMachine = "b" });

            Assert.AreEqual(1, SecretAccessLog.Prune(30, day.AddDays(400)));
            Assert.AreEqual(1, ConnectionAuditLog.ReadAll().Count, "the connection log is not this log's to delete");
        }

        [TestMethod]
        public void ClearRemovesEveryDayFileOfThisLogOnly()
        {
            var day = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
            using (var secrets = new SecretAccessLog())
            {
                secrets.AppendNow(Rec(day));
                secrets.AppendNow(Rec(day.AddDays(1)));
            }
            using (var connections = new ConnectionAuditLog())
                connections.AppendNow(new ConnectionAuditRecord { TimeUtc = day, ServerName = "web01", LocalUser = "a", LocalMachine = "b" });

            Assert.AreEqual(2, SecretAccessLog.Clear());
            Assert.AreEqual(0, SecretAccessLog.ReadAll().Count);
            Assert.AreEqual(1, ConnectionAuditLog.ReadAll().Count);
        }

        [TestMethod]
        public void PruningIgnoresFilesThatAreNotOurs()
        {
            using var log = new SecretAccessLog();
            var now = new DateTime(2026, 5, 30, 12, 0, 0, DateTimeKind.Utc);
            log.AppendNow(Rec(now));

            var stranger = Path.Combine(SecretAccessLog.DirectoryPath, "notes.txt");
            File.WriteAllText(stranger, "keep me");
            var oddName = Path.Combine(SecretAccessLog.DirectoryPath, "secrets-not-a-date.jsonl");
            File.WriteAllText(oddName, "keep me too");

            SecretAccessLog.Prune(1, now.AddDays(500));

            Assert.IsTrue(File.Exists(stranger));
            Assert.IsTrue(File.Exists(oddName));
        }

        [TestMethod]
        public void DisablingItStopsRecords()
        {
            using var log = new SecretAccessLog { Enabled = false };
            log.Record(Rec(DateTime.UtcNow));
            log.Dispose();
            Assert.AreEqual(0, SecretAccessLog.ReadAll().Count);
        }

        [TestMethod]
        public void QueuedRecordsAreFlushedByDispose()
        {
            var log = new SecretAccessLog();
            log.Record(Rec(new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc), name: "queued"));
            log.Dispose();

            var all = SecretAccessLog.ReadAll();
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual("queued", all[0].ServerName);
        }

        [TestMethod]
        public void RecordFillsInWhoWasAtTheKeyboard()
        {
            var log = new SecretAccessLog();
            var record = Rec(new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc));
            record.LocalUser = "";
            record.LocalMachine = "";
            log.Record(record);
            log.Dispose();

            var all = SecretAccessLog.ReadAll();
            Assert.AreEqual(1, all.Count);
            Assert.AreNotEqual("", all[0].LocalUser, "an audit line that does not say who is not an audit line");
            Assert.AreNotEqual("", all[0].LocalMachine);
        }

        [TestMethod]
        public void ExportWritesEveryRecordAsCsv()
        {
            using var log = new SecretAccessLog();
            log.AppendNow(Rec(new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc), name: "a"));
            log.AppendNow(Rec(new DateTime(2026, 5, 2, 9, 0, 0, DateTimeKind.Utc), name: "b"));

            var csvPath = Path.Combine(_root, "export", "audit-secrets.csv");
            Assert.AreEqual(2, SecretAccessLog.ExportCsv(csvPath));

            var text = File.ReadAllText(csvPath);
            StringAssert.StartsWith(text.TrimStart('\uFEFF'), SecretAccessCsv.Header);
            StringAssert.Contains(text, ",a,");
            StringAssert.Contains(text, ",b,");
        }

        /// <summary>
        /// The export button asks for one file name and writes two, so the second has to be derivable from
        /// the first and land beside it rather than in the working directory.
        /// </summary>
        [TestMethod]
        public void TheSiblingCsvSitsNextToTheOneTheUserNamed()
        {
            var chosen = Path.Combine(_root, "reports", "1Remote-audit-20260501-090000.csv");
            var sibling = SecretAccessLog.SiblingCsvPath(chosen);

            Assert.AreEqual(Path.GetDirectoryName(chosen), Path.GetDirectoryName(sibling));
            Assert.AreEqual("1Remote-audit-20260501-090000-secrets.csv", Path.GetFileName(sibling));
        }

        [TestMethod]
        public void ASiblingIsStillDerivedWhenTheNameHasNoExtensionOrSeveralDots()
        {
            Assert.AreEqual("audit-secrets", Path.GetFileName(SecretAccessLog.SiblingCsvPath("audit")));
            Assert.AreEqual("a.b-secrets.csv", Path.GetFileName(SecretAccessLog.SiblingCsvPath("a.b.csv")));
        }

        [TestMethod]
        public void ReadingAnAbsentFolderIsNotAnError()
        {
            Assert.AreEqual(0, SecretAccessLog.ReadAll().Count);
            Assert.AreEqual(0, SecretAccessLog.DayFiles().Count);
            Assert.AreEqual(0, SecretAccessLog.Prune(30, DateTime.UtcNow));
        }
    }
}
