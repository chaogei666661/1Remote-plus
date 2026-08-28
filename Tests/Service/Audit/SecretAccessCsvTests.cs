using System;
using System.Collections.Generic;
using _1RM.Service.Audit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Service.Audit
{
    /// <summary>
    /// The CSV an auditor opens. Two things have to hold: a row lines up with the header, and nothing that
    /// reached the log through a file dialog or a server name can execute when the file is opened in Excel.
    /// </summary>
    [TestClass]
    public class SecretAccessCsvTests
    {
        private static SecretAccessRecord ARecord() => new SecretAccessRecord
        {
            TimeUtc = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc),
            Event = ESecretAccessEvent.ServerListExported,
            LocalUser = "alice",
            LocalMachine = "LAPTOP",
            ServerName = "",
            Protocol = "",
            Address = "",
            RemoteUser = "",
            DataSource = "Shared MySQL",
            Count = 47,
            Destination = @"D:\share\everything.json",
            Note = "cleartext",
            ServerId = "",
        };

        [TestMethod]
        public void TheHeaderAndOneRowLineUp()
        {
            var line = SecretAccessCsv.Line(ARecord());
            Assert.AreEqual(SecretAccessCsv.Header.Split(',').Length, CountFields(line),
                "a row has to have as many fields as the header");
            StringAssert.Contains(line, "2026-03-04 05:06:07");
            StringAssert.Contains(line, "ServerListExported");
            StringAssert.Contains(line, "alice");
            StringAssert.Contains(line, "47");
            StringAssert.Contains(line, "everything.json");
        }

        [TestMethod]
        public void WriteEmitsAHeaderEvenWithNoRecords()
        {
            Assert.AreEqual(SecretAccessCsv.Header + "\r\n", SecretAccessCsv.Write(new List<SecretAccessRecord>()));
        }

        /// <summary>
        /// A destination is whatever the user typed into a save dialog, which can start with =, and it ends
        /// up in a file somebody else opens.
        /// </summary>
        [TestMethod]
        public void ADestinationThatExcelWouldRunAsAFormulaIsNeutralised()
        {
            var record = ARecord();
            record.Destination = "=cmd|'/c calc'!A1";
            StringAssert.Contains(SecretAccessCsv.Line(record), "'=cmd|'/c calc'!A1");
        }

        [TestMethod]
        public void ACommaInAPathDoesNotShiftEveryColumnAfterIt()
        {
            var record = ARecord();
            record.Destination = @"D:\a,b\export.json";
            Assert.AreEqual(SecretAccessCsv.Header.Split(',').Length, CountFields(SecretAccessCsv.Line(record)));
        }

        [TestMethod]
        public void NoPasswordShapedFieldExistsToLeak()
        {
            // The record type is the contract: if a secret is ever added to it, this fails.
            foreach (var property in typeof(SecretAccessRecord).GetProperties())
            {
                var name = property.Name.ToLowerInvariant();
                Assert.IsFalse(name.Contains("password") || name.Contains("secret") || name.Contains("privatekey"),
                    $"{property.Name} does not belong in an audit record");
            }
        }

        /// <summary>
        /// The five things that can send a credential out of the app. A new one added without a record kind
        /// is a hole in the log, and this is where it gets noticed.
        /// </summary>
        [TestMethod]
        public void EveryWayACredentialLeavesHasAnEventOfItsOwn()
        {
            CollectionAssert.AreEquivalent(
                new[]
                {
                    ESecretAccessEvent.PasswordCopied,
                    ESecretAccessEvent.ServerListExported,
                    ESecretAccessEvent.RdpFileExported,
                    ESecretAccessEvent.BackupCreated,
                    ESecretAccessEvent.AuditLogExported,
                },
                (ESecretAccessEvent[])Enum.GetValues(typeof(ESecretAccessEvent)));
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
