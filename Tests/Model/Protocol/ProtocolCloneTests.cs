using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using _1RM.Model.Protocol;
using _1RM.Model.Protocol.Base;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Model.Protocol
{
    /// <summary>
    /// Clone() is what the editor hands the user: the dialog edits a copy and only writes it back when the
    /// user accepts. Anything the copy still shares with the original is therefore edited live, behind the
    /// user's back, and survives pressing cancel — so these tests are about independence, not about equality.
    /// </summary>
    [TestClass]
    public class ProtocolCloneTests
    {
        [TestInitialize]
        public void Setup() => TestInit.Init();

        [TestMethod]
        public void EveryProtocol_HandsItsCloneItsOwnCollections()
        {
            var checkedAtLeastOne = false;

            foreach (var protocol in ProtocolBase.GetAllSubInstance())
            {
                var seeded = SeedCollections(protocol);
                var clone = protocol.Clone();

                foreach (var property in seeded)
                {
                    var mine = (IList)property.GetValue(protocol)!;
                    var theirs = (IList)property.GetValue(clone)!;
                    var countBefore = mine.Count;

                    theirs.Clear();

                    Assert.AreEqual(countBefore, mine.Count,
                        $"{protocol.GetType().Name}.{property.Name} is the very same collection on the clone, " +
                        "so editing the copy edits the server the user is still looking at");
                    checkedAtLeastOne = true;
                }
            }

            Assert.IsTrue(checkedAtLeastOne, "the sweep found nothing to check, which means it is not testing anything");
        }

        [TestMethod]
        public void Clone_GivesTheCopyItsOwnTagsAndTreeNodes()
        {
            var rdp = new RDP { DisplayName = "web-01", Address = "10.0.0.1", Tags = new List<string> { "prod" } };
            rdp.TreeNodes = new List<string> { "datacentre" };

            var clone = (RDP)rdp.Clone();
            clone.Tags.Add("scratch");
            clone.TreeNodes.Add("scratch");

            CollectionAssert.AreEqual(new[] { "prod" }, rdp.Tags);
            CollectionAssert.AreEqual(new[] { "datacentre" }, rdp.TreeNodes);
        }

        [TestMethod]
        public void Clone_GivesTheCopyItsOwnAlternateCredentials()
        {
            var ssh = new SSH { Address = "10.0.0.2" };
            ssh.AlternateCredentials.Add(new Credential { Name = "root", UserName = "root", Password = "hunter2" });

            var clone = (SSH)ssh.Clone();
            clone.AlternateCredentials.Add(new Credential { Name = "backup" });

            Assert.AreEqual(1, ssh.AlternateCredentials.Count, "a credential added to the copy leaked into the original");
        }

        [TestMethod]
        public void Clone_CopiesEachAlternateCredentialByValue()
        {
            var sftp = new SFTP { Address = "10.0.0.3" };
            sftp.AlternateCredentials.Add(new Credential { Name = "root", UserName = "root", Password = "hunter2" });

            var clone = (SFTP)sftp.Clone();
            clone.AlternateCredentials[0].UserName = "someone-else";

            Assert.AreEqual("root", sftp.AlternateCredentials[0].UserName,
                "the list was copied but the credentials in it were not, so editing one edits both");
        }

        [TestMethod]
        public void Clone_GivesAProtocolWithoutALoginItsOwnAlternateCredentials()
        {
            // The bug this guards: AlternateCredentials lives on ProtocolBaseWithAddressPort, but Clone only
            // copied it for the ...UserPwd subclass, so Telnet shared the collection with its copy.
            var telnet = new Telnet { Address = "10.0.0.4" };
            telnet.AlternateCredentials.Add(new Credential { Name = "console" });

            var clone = (Telnet)telnet.Clone();
            clone.AlternateCredentials.Clear();

            Assert.AreEqual(1, telnet.AlternateCredentials.Count);
        }

        [TestMethod]
        public void Clone_GivesAnAppItsOwnArgumentList()
        {
            var app = new LocalApp { ExePath = @"C:\tools\thing.exe" };
            app.ArgumentList.Add(new AppArgument { Name = "target", Key = "-t", Value = "10.0.0.5" });

            var clone = (LocalApp)app.Clone();
            clone.ArgumentList[0].Value = "somewhere-else";
            clone.ArgumentList.Add(new AppArgument { Name = "verbose", Key = "-v" });

            Assert.AreEqual(1, app.ArgumentList.Count);
            Assert.AreEqual("10.0.0.5", app.ArgumentList[0].Value, "the arguments were shared, not copied");
        }

        [TestMethod]
        public void CloningAnArgument_KeepsTheOptionThatWasChosen()
        {
            // Cloning used to assign the copied dictionary through the Selections setter, and that setter
            // re-picks Value for a selection argument — so the user's choice was thrown away on every copy.
            var argument = new AppArgument
            {
                Name = "colour",
                Type = AppArgumentType.Selection,
                IsNullable = false,
                Selections = new Dictionary<string, string> { { "red", "red" }, { "green", "green" } },
            };
            argument.Value = "red";

            var clone = (AppArgument)argument.Clone();

            Assert.AreEqual("red", clone.Value);
            CollectionAssert.AreEquivalent(argument.SelectionKeys, clone.SelectionKeys);
        }

        [TestMethod]
        public void CloningAnArgument_GivesTheCopyItsOwnSelections()
        {
            var argument = new AppArgument
            {
                Name = "colour",
                Type = AppArgumentType.Selection,
                Selections = new Dictionary<string, string> { { "red", "red" } },
            };

            var clone = (AppArgument)argument.Clone();
            clone.Selections.Add("blue", "blue");

            Assert.IsFalse(argument.Selections.ContainsKey("blue"));
        }

        [TestMethod]
        public void CloningAnArgument_GivesTheCopyItsOwnFilePicker()
        {
            // The command captures the argument that created it, so a copy holding the original's command
            // would write whatever file the user picks back into the original.
            var argument = new AppArgument { Name = "makefile", Type = AppArgumentType.File };
            var _ = argument.CmdSelectArgumentFile;

            var clone = (AppArgument)argument.Clone();

            Assert.IsFalse(ReferenceEquals(argument.CmdSelectArgumentFile, clone.CmdSelectArgumentFile));
        }

        [TestMethod]
        public void Clone_KeepsTheValuesTheUserTypedIn()
        {
            var rdp = new RDP
            {
                DisplayName = "web-01",
                Address = "10.0.0.1",
                Port = "13389",
                UserName = "admin",
                Password = "hunter2",
                Domain = "corp",
                Note = "the one behind the jump host",
            };
            rdp.Id = "01H000000000000000000000";

            var clone = (RDP)rdp.Clone();

            Assert.AreEqual(rdp.Id, clone.Id);
            Assert.AreEqual(rdp.DisplayName, clone.DisplayName);
            Assert.AreEqual(rdp.Address, clone.Address);
            Assert.AreEqual(rdp.Port, clone.Port);
            Assert.AreEqual(rdp.UserName, clone.UserName);
            Assert.AreEqual(rdp.Password, clone.Password);
            Assert.AreEqual(rdp.Domain, clone.Domain);
            Assert.AreEqual(rdp.Note, clone.Note);
        }

        /// <summary>
        /// Puts one element into every list-like property the protocol owns, so the sweep above has something
        /// to remove. Returns the properties it managed to fill.
        /// </summary>
        private static List<PropertyInfo> SeedCollections(ProtocolBase protocol)
        {
            var seeded = new List<PropertyInfo>();
            foreach (var property in protocol.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0) continue;
                if (!property.CanRead) continue;
                if (!typeof(IList).IsAssignableFrom(property.PropertyType)) continue;

                var elementType = property.PropertyType.IsGenericType
                    ? property.PropertyType.GetGenericArguments().FirstOrDefault()
                    : property.PropertyType.GetElementType();
                if (elementType == null) continue;
                if (!TryCreate(elementType, out var element)) continue;

                if (property.GetValue(protocol) is not IList list) continue;
                try
                {
                    list.Add(element);
                }
                catch (Exception)
                {
                    // a fixed-size or read-only list is not something a clone can leak
                    continue;
                }

                // Some getters rebuild their list on every read (Tags does), which is fine — what matters is
                // that the element stuck, otherwise removing it later proves nothing.
                if (property.GetValue(protocol) is IList reread && reread.Count > 0)
                    seeded.Add(property);
            }
            return seeded;
        }

        private static bool TryCreate(Type type, out object? instance)
        {
            if (type == typeof(string))
            {
                instance = "seed";
                return true;
            }

            try
            {
                // OptionalParamBinding, because the model types take a "is this editable" flag with a default.
                instance = Activator.CreateInstance(type,
                    BindingFlags.CreateInstance | BindingFlags.Public | BindingFlags.Instance | BindingFlags.OptionalParamBinding,
                    null, Array.Empty<object>(), CultureInfo.InvariantCulture);
                return instance != null;
            }
            catch (Exception)
            {
                instance = null;
                return false;
            }
        }
    }
}
