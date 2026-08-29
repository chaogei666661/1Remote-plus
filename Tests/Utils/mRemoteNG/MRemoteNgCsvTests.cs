using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using _1RM.Utils.mRemoteNG;

namespace Tests.Utils.mRemoteNG
{
    /// <summary>
    /// mRemoteNG's confCons.csv drops trailing empty columns, so a data row is routinely shorter than the
    /// header. The reader indexed the row by the header's column position, so the first such row threw
    /// IndexOutOfRange and aborted the whole import. These pin the fix and the surrounding behaviour
    /// (id de-duplication, container-path names, inheritance from parents).
    /// </summary>
    [TestClass]
    public class MRemoteNgCsvTests
    {
        // A realistic header: many columns, of which a row may fill only the first few.
        private const string Header =
            "Name;Id;Parent;NodeType;Username;Password;Hostname;Protocol;Port;Domain;Description";

        [TestMethod]
        public void ARowShorterThanTheHeaderDoesNotThrowAndReadsWhatIsThere()
        {
            // Only 8 fields present; the header has 11. The missing Domain/Description must read as "".
            var lines = new[]
            {
                Header,
                "web01;id-1;;Connection;alice;secret;10.0.0.5;RDP",
            };

            var items = MRemoteNgCsv.ParseItems(lines);
            Assert.IsNotNull(items);
            var item = items!["id-1"];
            Assert.AreEqual("web01", item.Name);
            Assert.AreEqual("alice", item.Username);
            Assert.AreEqual("10.0.0.5", item.Hostname);
            Assert.AreEqual("RDP", item.Protocol);
            Assert.AreEqual("", item.Domain);       // absent column, no crash
            Assert.AreEqual("", item.Description);
        }

        [TestMethod]
        public void PresentColumnsAreReadAndTrimmed()
        {
            var lines = new[]
            {
                Header,
                "box;id-9;;Connection;  bob  ; pw ;host.example.com;SSH2;2222;CORP;a note",
            };

            var item = MRemoteNgCsv.ParseItems(lines)!["id-9"];
            Assert.AreEqual("bob", item.Username);   // trimmed
            Assert.AreEqual("pw", item.Password);
            Assert.AreEqual("2222", item.Port);
            Assert.AreEqual("CORP", item.Domain);
        }

        [TestMethod]
        public void DuplicateIdsAreDisambiguatedInsteadOfThrowing()
        {
            var lines = new[]
            {
                Header,
                "a;dup;;Connection;;;10.0.0.1;RDP",
                "b;dup;;Connection;;;10.0.0.2;RDP",
                "c;dup;;Connection;;;10.0.0.3;RDP",
            };

            var items = MRemoteNgCsv.ParseItems(lines)!;
            Assert.AreEqual(3, items.Count);
            Assert.IsTrue(items.ContainsKey("dup"));
            Assert.IsTrue(items.ContainsKey("dup (1)"));
            Assert.IsTrue(items.ContainsKey("dup (2)"));
        }

        [TestMethod]
        public void AConnectionNameIsPrefixedWithItsContainerPath()
        {
            var lines = new[]
            {
                Header,
                "Prod;c-root;;Container;;;;;",
                "DB;c-db;c-root;Container;;;;;",
                "pg01;s-1;c-db;Connection;;;10.0.0.7;RDP",
            };

            var item = MRemoteNgCsv.ParseItems(lines)!["s-1"];
            Assert.AreEqual("Prod - DB - pg01", item.Name);
        }

        [TestMethod]
        public void AConnectionInheritsEmptyFieldsFromTheNearestAncestorThatHasThem()
        {
            var lines = new[]
            {
                Header,
                "Prod;c-root;;Container;root-user;;;;;CORP",
                "pg01;s-1;c-root;Connection;;;10.0.0.7;RDP",
            };

            var items = MRemoteNgCsv.ParseItems(lines)!;
            MRemoteNgCsv.Inherit(items);

            var item = items["s-1"];
            Assert.AreEqual("root-user", item.Username); // inherited from the container
            Assert.AreEqual("CORP", item.Domain);        // inherited
            Assert.AreEqual("10.0.0.7", item.Hostname);  // its own value kept
        }

        [TestMethod]
        public void AnEmptyOrHeaderOnlyFileReturnsNothingRatherThanThrowing()
        {
            Assert.IsNull(MRemoteNgCsv.ParseItems(new string[0]));
            var headerOnly = MRemoteNgCsv.ParseItems(new[] { Header });
            Assert.IsNotNull(headerOnly);
            Assert.AreEqual(0, headerOnly!.Count);
        }
    }
}
