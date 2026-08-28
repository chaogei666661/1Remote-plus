using System;
using System.Collections.Generic;
using _1RM.Service.Audit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Service.Audit
{
    [TestClass]
    public class AuditCsvTests
    {
        [TestMethod]
        public void ACommaOrAQuoteIsQuotedTheRfcWay()
        {
            Assert.AreEqual("\"a,b\"", AuditCsv.Escape("a,b"));
            Assert.AreEqual("\"say \"\"hi\"\"\"", AuditCsv.Escape("say \"hi\""));
            Assert.AreEqual("\"two\nlines\"", AuditCsv.Escape("two\nlines"));
            Assert.AreEqual("plain", AuditCsv.Escape("plain"));
            Assert.AreEqual("", AuditCsv.Escape(null));
        }

        [TestMethod]
        public void AFieldThatExcelWouldRunAsAFormulaIsNeutralised()
        {
            // A server name is free text typed by whoever owns the server list, which for a shared data
            // source need not be the person opening the export.
            Assert.AreEqual("'=cmd|'/c calc'!A1", AuditCsv.Escape("=cmd|'/c calc'!A1"));
            Assert.AreEqual("'+1234", AuditCsv.Escape("+1234"));
            Assert.AreEqual("'-1234", AuditCsv.Escape("-1234"));
            Assert.AreEqual("'@SUM(A1)", AuditCsv.Escape("@SUM(A1)"));
        }

        [TestMethod]
        public void ANeutralisedFieldIsStillQuotedWhenItAlsoNeedsIt()
        {
            Assert.AreEqual("\"'=a,b\"", AuditCsv.Escape("=a,b"));
        }

        [TestMethod]
        public void ANormalNegativeNumberIsNotMangledIntoSomethingElse()
        {
            // Prefixing is visible, so the value is still readable; what matters is that nothing is lost.
            var escaped = AuditCsv.Escape("-1");
            StringAssert.Contains(escaped, "-1");
        }

        [TestMethod]
        public void TheHeaderAndOneRowLineUp()
        {
            var record = new ConnectionAuditRecord
            {
                TimeUtc = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc),
                Event = EAuditEvent.SessionOpened,
                Protocol = "SSH",
                ServerName = "web01",
                Address = "10.0.0.5",
                Port = 22,
                RemoteUser = "deploy",
                DataSource = "Local",
                Proxy = "bastion",
                Reason = "",
                DurationSeconds = 0,
                LocalUser = "alice",
                LocalMachine = "LAPTOP",
                ServerId = "abc",
                ConnectionId = "abc",
            };

            var line = AuditCsv.Line(record);
            Assert.AreEqual(AuditCsv.Header.Split(',').Length, CountFields(line),
                "a row has to have as many fields as the header");
            StringAssert.Contains(line, "2026-03-04 05:06:07");
            StringAssert.Contains(line, "SessionOpened");
            StringAssert.Contains(line, "10.0.0.5");
            StringAssert.Contains(line, "deploy");
        }

        [TestMethod]
        public void NoPasswordShapedFieldExistsToLeak()
        {
            // The record type is the contract: if a secret is ever added to it, this fails.
            foreach (var property in typeof(ConnectionAuditRecord).GetProperties())
            {
                var name = property.Name.ToLowerInvariant();
                Assert.IsFalse(name.Contains("password") || name.Contains("secret") || name.Contains("privatekey"),
                    $"{property.Name} does not belong in an audit record");
            }
        }

        [TestMethod]
        public void WriteEmitsAHeaderEvenWithNoRecords()
        {
            var csv = AuditCsv.Write(new List<ConnectionAuditRecord>());
            Assert.AreEqual(AuditCsv.Header + "\r\n", csv);
        }

        private static int CountFields(string line)
        {
            var count = 1;
            var inQuotes = false;
            foreach (var c in line)
            {
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes) ++count;
            }
            return count;
        }
    }
}
