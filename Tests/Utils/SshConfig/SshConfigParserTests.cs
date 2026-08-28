using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using _1RM.Utils.Proxy;
using _1RM.Utils.SshConfig;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.SshConfig
{
    [TestClass]
    public class SshConfigParserTests
    {
        private readonly List<string> _tempDirectories = new List<string>();

        [TestInitialize]
        public void Setup() => TestInit.Init();

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var directory in _tempDirectories)
            {
                try { Directory.Delete(directory, true); }
                catch { /* a leftover temp directory is not worth failing a test over */ }
            }
            _tempDirectories.Clear();
        }

        private static string[] Lines(params string[] lines) => lines;

        /// <summary>
        /// A throwaway ~/.ssh, because Include is about the file system: which paths a relative name
        /// resolves against, what a glob expands to, and what happens when two files include each other.
        /// A stub of the file system would only assert that the stub was called.
        /// </summary>
        private string NewSshDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "1rm-sshconfig-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            _tempDirectories.Add(directory);
            return directory;
        }

        private static string Write(string directory, string name, params string[] lines)
        {
            var path = Path.Combine(directory, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, lines);
            return path;
        }

        [TestMethod]
        public void ABlockIsReadIntoAConnectableEntry()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host web",
                "    HostName 10.0.0.5",
                "    User deploy",
                "    Port 2222"));

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("web", entries[0].Alias);
            Assert.AreEqual("10.0.0.5", entries[0].HostName);
            Assert.AreEqual("deploy", entries[0].User);
            Assert.AreEqual(2222, entries[0].Port);
        }

        [TestMethod]
        public void AnAliasWithNoHostNameConnectsToItself()
        {
            var entries = SshConfigParser.Parse(Lines("Host build.example.com"));

            Assert.AreEqual("build.example.com", entries[0].HostName);
            Assert.AreEqual(22, entries[0].Port, "the ssh default applies when no port is given");
        }

        [TestMethod]
        public void WildcardBlocksAreDefaultsRatherThanMachines()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host *",
                "    User someone",
                "Host web?",
                "    HostName 10.0.0.5",
                "Host real",
                "    HostName 10.0.0.9"));

            Assert.AreEqual(1, entries.Count, "only the block naming an actual host survives");
            Assert.AreEqual("real", entries[0].Alias);
            Assert.AreEqual("10.0.0.9", entries[0].HostName, "the pattern block must not win over the host's own");
            Assert.AreEqual("someone", entries[0].User, "but its defaults still apply, as they do to ssh");
        }

        [TestMethod]
        public void APatternBlockAppliesOnlyToTheAliasesItMatches()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host *.internal",
                "    User ops",
                "    Port 2222",
                "Host db.internal",
                "Host laptop"));

            var db = entries.Single(x => x.Alias == "db.internal");
            var laptop = entries.Single(x => x.Alias == "laptop");

            Assert.AreEqual("ops", db.User);
            Assert.AreEqual(2222, db.Port);
            Assert.AreEqual("", laptop.User, "laptop does not match *.internal");
            Assert.AreEqual(22, laptop.Port);
        }

        [TestMethod]
        public void ANegatedPatternExcludesTheAliasFromTheDefaults()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host !jump *",
                "    User deploy",
                "Host jump",
                "Host app"));

            Assert.AreEqual("", entries.Single(x => x.Alias == "jump").User);
            Assert.AreEqual("deploy", entries.Single(x => x.Alias == "app").User);
        }

        [TestMethod]
        public void ADefaultBlockBelowAHostDoesNotOverrideWhatTheHostAlreadySet()
        {
            // Order is the whole rule in ssh: first value wins across the file, so where the "Host *" block
            // sits decides whether it is a default or an override.
            var entries = SshConfigParser.Parse(Lines(
                "Host web",
                "    User deploy",
                "Host *",
                "    User someone"));

            Assert.AreEqual("deploy", entries.Single().User);
        }

        [TestMethod]
        public void ADefaultBlockAboveAHostDoesOverrideIt()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host *",
                "    User someone",
                "Host web",
                "    User deploy"));

            Assert.AreEqual("someone", entries.Single().User,
                "surprising, but it is what ssh does and an import that disagreed would connect as the wrong user");
        }

        [TestMethod]
        public void TheSameAliasNamedTwiceIsOneServerBuiltFromBothBlocks()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host web",
                "    HostName 10.0.0.5",
                "Host web",
                "    User deploy"));

            var entry = entries.Single();
            Assert.AreEqual("10.0.0.5", entry.HostName);
            Assert.AreEqual("deploy", entry.User);
        }

        [TestMethod]
        public void PercentHInHostNameBecomesTheAlias()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host *.example",
                "    HostName %h.internal",
                "Host db.example"));

            Assert.AreEqual("db.example.internal", entries.Single().HostName);
        }

        [TestMethod]
        public void MatchSectionsAreSkippedBecauseTheirConditionsCannotBeEvaluatedHere()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host web",
                "    HostName 10.0.0.5",
                "Match exec \"test -f /tmp/x\"",
                "    User ghost",
                "    Port 1234"));

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("", entries[0].User, "the Match block must not leak onto the previous host");
            Assert.AreEqual(22, entries[0].Port);
        }

        [TestMethod]
        public void MatchAllAppliesToEveryHost()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host a",
                "Host b",
                "Match all",
                "    User shared"));

            Assert.IsTrue(entries.All(x => x.User == "shared"));
        }

        [TestMethod]
        public void MatchHostTestsTheAddressAndMatchOriginalHostTestsTheAlias()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host box",
                "    HostName 10.0.0.5",
                "Match host 10.0.0.*",
                "    User byaddress",
                "Match originalhost box",
                "    Port 2222"));

            var entry = entries.Single();
            Assert.AreEqual("byaddress", entry.User);
            Assert.AreEqual(2222, entry.Port);
        }

        [TestMethod]
        public void AMatchHostThatDoesNotMatchContributesNothing()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host box",
                "    HostName 10.0.0.5",
                "Match host *.internal",
                "    User ghost"));

            Assert.AreEqual("", entries.Single().User);
        }

        [TestMethod]
        public void ANegatedMatchHostAppliesToEverythingElse()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host a",
                "    HostName 10.0.0.5",
                "Host b",
                "    HostName 192.168.0.5",
                "Match !host 10.0.0.*",
                "    User outside"));

            Assert.AreEqual("", entries.Single(x => x.Alias == "a").User);
            Assert.AreEqual("outside", entries.Single(x => x.Alias == "b").User);
        }

        [TestMethod]
        public void AMatchOnSomethingWeCannotDecideIsSkippedWholeRatherThanPartlyApplied()
        {
            foreach (var criteria in new[] { "Match user deploy", "Match localnetwork 10.0.0.0/8", "Match canonical", "Match tagged prod" })
            {
                var entries = SshConfigParser.Parse(Lines(
                    "Host web",
                    criteria,
                    "    User ghost"));

                Assert.AreEqual("", entries.Single().User, criteria + " should contribute nothing");
            }
        }

        [TestMethod]
        public void AQuotedPatternWithASpaceIsOneAlias()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host \"my box\"",
                "    HostName 10.0.0.5"));

            Assert.AreEqual("my box", entries.Single().Alias);
        }

        [TestMethod]
        public void OneHostLineCanNameSeveralAliasesAndAllOfThemGetTheSettings()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host a b",
                "    User shared",
                "    Port 2200"));

            Assert.AreEqual(2, entries.Count);
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, entries.Select(x => x.Alias).ToList());
            Assert.IsTrue(entries.All(x => x.User == "shared" && x.Port == 2200));
        }

        [TestMethod]
        public void TheFirstValueWinsAsItDoesInSsh()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host web",
                "    Port 2222",
                "    Port 9999"));

            Assert.AreEqual(2222, entries[0].Port);
        }

        [TestMethod]
        public void CommentsBlankLinesAndEqualsSeparatorsAreAllHandled()
        {
            var entries = SshConfigParser.Parse(Lines(
                "# a comment",
                "",
                "Host web",
                "   # indented comment",
                "   HostName=10.0.0.5",
                "   Port = 2222"));

            Assert.AreEqual("10.0.0.5", entries[0].HostName);
            Assert.AreEqual(2222, entries[0].Port);
        }

        [TestMethod]
        public void ASingleProxyJumpHopIsCaptured()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host internal",
                "    HostName 10.0.0.5",
                "    ProxyJump bastion"));

            Assert.AreEqual("bastion", entries[0].ProxyJump);
        }

        [TestMethod]
        public void AProxyJumpHopKeepsOnlyTheAliasThatNamesTheBlock()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host internal",
                "    ProxyJump ops@bastion:2222"));

            Assert.AreEqual("bastion", entries[0].ProxyJump);
        }

        [TestMethod]
        public void AChainOfJumpsIsLeftAloneBecauseOnlyOneHopCanBeRepresented()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host internal",
                "    ProxyJump first,second"));

            Assert.AreEqual("", entries[0].ProxyJump);
        }

        [TestMethod]
        public void ProxyJumpNoneIsNotAHost()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host internal",
                "    ProxyJump none"));

            Assert.AreEqual("", entries[0].ProxyJump);
        }

        [TestMethod]
        public void ImportingBuildsServersAndTheJumpHostTheyNeed()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host bastion",
                "    HostName jump.example.com",
                "    User ops",
                "    Port 2222",
                "Host internal",
                "    HostName 10.0.0.5",
                "    User deploy",
                "    ProxyJump bastion"));

            var result = SshConfigImporter.Build(entries);

            Assert.AreEqual(2, result.Servers.Count);
            Assert.AreEqual(1, result.CreatedProxies.Count);

            var proxy = result.CreatedProxies[0];
            Assert.AreEqual(EProxyType.SshJump, proxy.Type);
            Assert.AreEqual("jump.example.com", proxy.Address);
            Assert.AreEqual(2222, proxy.Port);
            Assert.AreEqual("ops", proxy.UserName);

            var internalServer = result.Servers.Single(x => x.DisplayName == "internal");
            Assert.AreEqual(proxy.Name, internalServer.ProxyName, "the server should route through it");
        }

        [TestMethod]
        public void ReimportingReusesAJumpHostAlreadyOnThePage()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host bastion",
                "    HostName jump.example.com",
                "Host internal",
                "    HostName 10.0.0.5",
                "    ProxyJump bastion"));

            var already = new ProxyConfig
            {
                Name = SshConfigImporter.PROXY_NAME_PREFIX + "bastion",
                Type = EProxyType.SshJump,
                Address = "jump.example.com",
                UserName = "ops",
            };

            var result = SshConfigImporter.Build(entries, existingProxies: new[] { already });

            Assert.AreEqual(0, result.CreatedProxies.Count, "importing twice should not pile up duplicates");
            Assert.AreEqual(already.Name, result.Servers.Single(x => x.DisplayName == "internal").ProxyName);
        }

        [TestMethod]
        public void AnIncludedFileContributesItsHosts()
        {
            var ssh = NewSshDirectory();
            Write(ssh, "work", "Host work-db", "    HostName 10.0.0.5");
            var config = Write(ssh, "config", "Include work", "Host laptop");

            var entries = SshConfigParser.ParseFile(config);

            CollectionAssert.AreEquivalent(new[] { "work-db", "laptop" }, entries.Select(x => x.Alias).ToList());
            Assert.AreEqual("10.0.0.5", entries.Single(x => x.Alias == "work-db").HostName);
        }

        [TestMethod]
        public void AnIncludeGlobExpandsInLexicalOrder()
        {
            var ssh = NewSshDirectory();
            Write(ssh, Path.Combine("config.d", "20-b"), "Host b");
            Write(ssh, Path.Combine("config.d", "10-a"), "Host a");
            var config = Write(ssh, "config", "Include config.d/*");

            var entries = SshConfigParser.ParseFile(config);

            CollectionAssert.AreEqual(new[] { "a", "b" }, entries.Select(x => x.Alias).ToList(),
                "ssh_config(5) promises lexical order, and order is what decides which value wins");
        }

        [TestMethod]
        public void AnIncludeIsSplicedInPlaceSoOrderStillDecidesTheWinner()
        {
            var ssh = NewSshDirectory();
            Write(ssh, "defaults", "Host *", "    User fromInclude");
            var config = Write(ssh, "config",
                "Host early",
                "    User setBeforeTheInclude",
                "Include defaults",
                "Host late");

            var entries = SshConfigParser.ParseFile(config);

            Assert.AreEqual("setBeforeTheInclude", entries.Single(x => x.Alias == "early").User);
            Assert.AreEqual("fromInclude", entries.Single(x => x.Alias == "late").User);
        }

        [TestMethod]
        public void SettingsAfterAnIncludeStillBelongToTheBlockThatWasOpen()
        {
            var ssh = NewSshDirectory();
            Write(ssh, "snippet", "# nothing but a comment");
            var config = Write(ssh, "config",
                "Host web",
                "    HostName 10.0.0.5",
                "Include snippet",
                "    User deploy");

            var entry = SshConfigParser.ParseFile(config).Single();

            Assert.AreEqual("10.0.0.5", entry.HostName);
            Assert.AreEqual("deploy", entry.User, "the Host block is still open on the other side of the Include");
        }

        [TestMethod]
        public void AnIncludeCycleTerminates()
        {
            var ssh = NewSshDirectory();
            Write(ssh, "a", "Include b", "Host from-a");
            Write(ssh, "b", "Include a", "Host from-b");
            var config = Write(ssh, "config", "Include a");

            var entries = SshConfigParser.ParseFile(config);

            CollectionAssert.AreEquivalent(new[] { "from-b", "from-a" }, entries.Select(x => x.Alias).ToList(),
                "each file is read once; the second visit is dropped rather than followed");
        }

        [TestMethod]
        public void AnIncludeOfSomethingThatIsNotThereIsIgnored()
        {
            var ssh = NewSshDirectory();
            var config = Write(ssh, "config", "Include nope", "Include also/missing/*", "Host web");

            Assert.AreEqual("web", SshConfigParser.ParseFile(config).Single().Alias,
                "a stale Include must not cost the user the rest of the file");
        }

        [TestMethod]
        public void AnAbsoluteIncludeIsUsedAsGiven()
        {
            var ssh = NewSshDirectory();
            var elsewhere = NewSshDirectory();
            var included = Write(elsewhere, "extra", "Host far-away");
            var config = Write(ssh, "config", "Include " + included);

            Assert.AreEqual("far-away", SshConfigParser.ParseFile(config).Single().Alias);
        }

        [TestMethod]
        public void TheAliasBecomesTheDisplayNameAndTheHostNameTheAddress()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host prod-db",
                "    HostName 10.0.0.7"));

            var server = SshConfigImporter.Build(entries).Servers.Single();

            Assert.AreEqual("prod-db", server.DisplayName, "the alias is what the user recognises");
            Assert.AreEqual("10.0.0.7", ((_1RM.Model.Protocol.SSH)server).Address);
        }
    }
}
