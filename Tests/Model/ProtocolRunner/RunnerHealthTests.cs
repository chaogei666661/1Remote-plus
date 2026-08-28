using System.Collections.Generic;
using System.Linq;
using _1RM.Model.ProtocolRunner;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Model.ProtocolRunner
{
    [TestClass]
    public class RunnerHealthTests
    {
        [TestInitialize]
        public void Setup() => TestInit.Init();

        private static readonly string[] SshMacros =
        {
            "%1RM_HOSTNAME%", "%1RM_PORT%", "%1RM_USERNAME%", "%1RM_PASSWORD%",
            "%1RM_PRIVATE_KEY_PATH%", "%SSH_VERSION%", "%STARTUP_AUTO_COMMAND%",
        };

        private static RunnerHealthInput Runner(string arguments, string? privateKeyArguments = null, params string[] environment) =>
            new RunnerHealthInput
            {
                ExePath = @"C:\Program Files\PuTTY\putty.exe",
                ExeExists = true,
                Arguments = arguments,
                ArgumentsForPrivateKey = privateKeyArguments,
                EnvironmentVariableValues = environment,
                KnownMacros = SshMacros,
            };

        private static List<string> Macros(string template) =>
            RunnerHealth.UnresolvedMacros(template, SshMacros.Select(x => x.Trim('%')).ToList()).ToList();

        [TestMethod]
        public void AWorkingRunnerHasNothingToReport()
        {
            var issues = RunnerHealth.Inspect(Runner(
                @"-ssh %1RM_HOSTNAME% -P %1RM_PORT% -l %1RM_USERNAME% -pw %1RM_PASSWORD% -%SSH_VERSION%",
                @"-ssh %1RM_HOSTNAME% -P %1RM_PORT% -l %1RM_USERNAME% -i %1RM_PRIVATE_KEY_PATH%"));

            Assert.AreEqual(0, issues.Count, string.Join("; ", issues.Select(x => x.TranslationKey + ":" + x.Detail)));
        }

        [TestMethod]
        public void AnEmptyExePathIsReportedDifferentlyFromOneThatIsSimplyNotThere()
        {
            var missing = RunnerHealth.Inspect(new RunnerHealthInput { ExePath = "   " });
            Assert.AreEqual(ERunnerIssue.ExePathMissing, missing.Single().Kind);

            var gone = RunnerHealth.Inspect(new RunnerHealthInput { ExePath = @"D:\PuTTY.exe", ExeExists = false });
            var issue = gone.Single();
            Assert.AreEqual(ERunnerIssue.ExeNotFound, issue.Kind);
            Assert.AreEqual(@"D:\PuTTY.exe", issue.Detail, "the message has to name the path the user has to fix");
        }

        [TestMethod]
        public void AMistypedMacroIsFoundAndNamed()
        {
            var issues = RunnerHealth.Inspect(Runner(@"-ssh %1RM_HOSTNAM% -P %1RM_PORT%"));

            var issue = issues.Single();
            Assert.AreEqual(ERunnerIssue.UnknownMacro, issue.Kind);
            Assert.AreEqual("1RM_HOSTNAM", issue.Detail);
        }

        [TestMethod]
        public void TheRenamedMacroIsStillWrongInACommandLine()
        {
            // %SSH_PRIVATE_KEY_PATH% became %1RM_PRIVATE_KEY_PATH% in 2023. It is still rewritten when the
            // environment is built, but never in a command line.
            Assert.AreEqual("SSH_PRIVATE_KEY_PATH",
                RunnerHealth.Inspect(Runner(@"-i %SSH_PRIVATE_KEY_PATH%")).Single().Detail);
        }

        [TestMethod]
        public void TheRenamedMacroIsAcceptedInAnEnvironmentVariableBecauseItStillWorksThere()
        {
            var issues = RunnerHealth.Inspect(Runner(@"-ssh %1RM_HOSTNAME%", null, "%SSH_PRIVATE_KEY_PATH%"));

            Assert.AreEqual(0, issues.Count);
        }

        [TestMethod]
        public void AMacroIsAlsoCheckedInThePrivateKeyCommandLineAndInEnvironmentVariables()
        {
            var issues = RunnerHealth.Inspect(Runner(
                @"-ssh %1RM_HOSTNAME%",
                @"-i %PRIVATE_KEY%",
                "%VNC_PASSWORD_X%"));

            CollectionAssert.AreEquivalent(
                new[] { "PRIVATE_KEY", "VNC_PASSWORD_X" },
                issues.Where(x => x.Kind == ERunnerIssue.UnknownMacro).Select(x => x.Detail).ToList());
        }

        [TestMethod]
        public void TheSameMistakeTwiceIsReportedOnce()
        {
            var issues = RunnerHealth.Inspect(Runner(@"%WHAT_IS_THIS% and again %WHAT_IS_THIS%"));

            Assert.AreEqual(1, issues.Count);
        }

        [TestMethod]
        public void PercentEncodingInASessionUrlIsNotAMacro()
        {
            // WinSCP wants the password percent-encoded, so "%25" and "%3A" turn up next to each other and
            // a naive scan reads "25ss%3" as a token. Crying wolf on a command line that works is worse
            // than missing a typo.
            CollectionAssert.AreEqual(new string[0],
                Macros("sftp://user:pa%25ss%3Aword@host"));
        }

        [TestMethod]
        public void AdjacentMacrosAreNotReadAsAThirdOneBetweenThem()
        {
            CollectionAssert.AreEqual(new string[0],
                Macros("%1RM_USERNAME%%1RM_PASSWORD%"));
        }

        [TestMethod]
        public void ATrailingUnclosedPercentIsNotAMacro()
        {
            CollectionAssert.AreEqual(new string[0], Macros("100% of the time"));
            CollectionAssert.AreEqual(new string[0], Macros("-l %1RM_USERNAME% 50%"));
        }

        [TestMethod]
        public void AnEmptyPrivateKeyCommandLineIsReportedBecauseItReplacesRatherThanExtendsTheOther()
        {
            var issues = RunnerHealth.Inspect(Runner(@"-ssh %1RM_HOSTNAME%", ""));

            Assert.AreEqual(ERunnerIssue.PrivateKeyArgumentsMissing, issues.Single().Kind);
        }

        [TestMethod]
        public void AProtocolWithNoPrivateKeyCommandLineIsNotAskedAboutOne()
        {
            var issues = RunnerHealth.Inspect(Runner(@"%1RM_HOSTNAME%:%1RM_PORT%", privateKeyArguments: null));

            Assert.AreEqual(0, issues.Count);
        }

        [TestMethod]
        public void EveryIssueHasAMessageToShow()
        {
            var issues = RunnerHealth.Inspect(new RunnerHealthInput
            {
                ExePath = "",
                Arguments = "%NOT_A_MACRO%",
                ArgumentsForPrivateKey = "",
                KnownMacros = SshMacros,
            });

            Assert.AreEqual(3, issues.Count);
            Assert.IsTrue(issues.All(x => x.TranslationKey.StartsWith("runner_health_")));
        }
    }
}
