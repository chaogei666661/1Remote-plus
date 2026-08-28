using System;
using System.Collections.Generic;
using System.Linq;

namespace _1RM.Model.ProtocolRunner
{
    public enum ERunnerIssue
    {
        /// <summary>No program has been chosen for this runner at all.</summary>
        ExePathMissing,

        /// <summary>A program has been chosen and it is not there.</summary>
        ExeNotFound,

        /// <summary>
        /// A <c>%LIKE_THIS%</c> placeholder that no macro will replace, so it reaches the program verbatim.
        /// </summary>
        UnknownMacro,

        /// <summary>
        /// An SSH or SFTP runner with a private-key command line left blank. This is the quiet one: the
        /// blank template does not fall back to the normal one, it replaces it, so a server that has a key
        /// configured launches the program with no arguments whatsoever.
        /// </summary>
        PrivateKeyArgumentsMissing,
    }

    public readonly struct RunnerIssue
    {
        public ERunnerIssue Kind { get; }

        /// <summary>The path or the macro the issue is about; empty when the kind says it all.</summary>
        public string Detail { get; }

        /// <summary>Which of the runner's command lines it was found in, for the message.</summary>
        public string Where { get; }

        public RunnerIssue(ERunnerIssue kind, string detail = "", string where = "")
        {
            Kind = kind;
            Detail = detail;
            Where = where;
        }

        /// <summary>Key into the language dictionaries; the arguments are <see cref="Detail"/>, <see cref="Where"/>.</summary>
        public string TranslationKey => Kind switch
        {
            ERunnerIssue.ExePathMissing => "runner_health_exe_missing",
            ERunnerIssue.ExeNotFound => "runner_health_exe_not_found",
            ERunnerIssue.UnknownMacro => "runner_health_unknown_macro",
            _ => "runner_health_private_key_arguments_missing",
        };
    }

    /// <summary>What <see cref="RunnerHealth.Inspect"/> needs, without reaching for a runner or a disk.</summary>
    public sealed class RunnerHealthInput
    {
        public string ExePath { get; set; } = "";
        public bool ExeExists { get; set; }
        public string Arguments { get; set; } = "";

        /// <summary>Null when the runner has no private-key command line, which is most protocols.</summary>
        public string? ArgumentsForPrivateKey { get; set; }

        public IReadOnlyList<string> EnvironmentVariableValues { get; set; } = Array.Empty<string>();

        /// <summary>The macros this protocol offers, with or without the surrounding percent signs.</summary>
        public IReadOnlyList<string> KnownMacros { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Looks over an external runner's configuration and reports what will go wrong before a session does.
    ///
    /// Until now the two ways to find out were the modal "Exe file ... does not existed!" at the moment you
    /// tried to connect, and — for a mistyped macro — nothing at all: the runner is started with
    /// <c>UseShellExecute = false</c>, so Windows expands nothing, and a stray <c>%1RM_HOSTNAM%</c> is
    /// handed to PuTTY as those literal characters. What the user sees is a program that opens and fails
    /// to connect, with no indication that the command line is the reason.
    /// </summary>
    public static class RunnerHealth
    {
        public static IReadOnlyList<RunnerIssue> Inspect(RunnerHealthInput input)
        {
            var issues = new List<RunnerIssue>();

            if (string.IsNullOrWhiteSpace(input.ExePath))
                issues.Add(new RunnerIssue(ERunnerIssue.ExePathMissing));
            else if (!input.ExeExists)
                issues.Add(new RunnerIssue(ERunnerIssue.ExeNotFound, input.ExePath.Trim()));

            var known = new HashSet<string>(
                input.KnownMacros.Select(x => (x ?? "").Trim().Trim('%')).Where(x => x.Length > 0),
                StringComparer.Ordinal);

            void Scan(string? template, string where, ICollection<string> accepted)
            {
                foreach (var macro in UnresolvedMacros(template, accepted))
                    issues.Add(new RunnerIssue(ERunnerIssue.UnknownMacro, macro, where));
            }

            Scan(input.Arguments, "arguments", known);
            Scan(input.ArgumentsForPrivateKey, "arguments_for_private_key", known);

            // RunnerHelper still rewrites the pre-2023 name when it builds the environment, and only there,
            // so a variable using it does work and must not be reported.
            var knownInEnvironment = new HashSet<string>(known, StringComparer.Ordinal) { "SSH_PRIVATE_KEY_PATH" };
            foreach (var value in input.EnvironmentVariableValues)
                Scan(value, "environment_variables", knownInEnvironment);

            if (input.ArgumentsForPrivateKey != null
                && input.ArgumentsForPrivateKey.Trim().Length == 0
                && input.Arguments.Trim().Length > 0)
                issues.Add(new RunnerIssue(ERunnerIssue.PrivateKeyArgumentsMissing));

            return issues;
        }

        public static IReadOnlyList<RunnerIssue> Inspect(ExternalRunner runner)
        {
            return Inspect(new RunnerHealthInput
            {
                ExePath = runner.ExePath,
                ExeExists = runner.IsExeExisted,
                Arguments = runner.Arguments,
                ArgumentsForPrivateKey = runner is ExternalRunnerForSSH ssh ? ssh.ArgumentsForPrivateKey : null,
                EnvironmentVariableValues = runner.EnvironmentVariables.Select(x => x.Value ?? "").ToList(),
                KnownMacros = runner.MarcoNames,
            });
        }

        /// <summary>
        /// Placeholders in <paramref name="template"/> that no macro will replace, in the order they appear
        /// and without duplicates.
        ///
        /// A token counts as a placeholder only when it is delimited by percent signs, contains nothing but
        /// letters, digits and underscores, and contains at least one underscore. Every macro the app
        /// defines has an underscore — <c>1RM_HOSTNAME</c>, <c>SSH_VERSION</c>, <c>STARTUP_PATH</c> — and
        /// requiring one is what keeps the percent-encoding in a WinSCP session URL
        /// (<c>pa%25ss%3Aword</c>, where "25ss%3" sits between two percent signs) from being reported as a
        /// broken macro. The cost is that a typo which also loses the underscore goes unnoticed; the
        /// alternative was crying wolf on a command line that works.
        /// </summary>
        public static IReadOnlyList<string> UnresolvedMacros(string? template, ICollection<string> knownMacros)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(template)) return found;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < template!.Length; i++)
            {
                if (template[i] != '%') continue;

                var close = template.IndexOf('%', i + 1);
                if (close < 0) break;

                var token = template.Substring(i + 1, close - i - 1);
                if (!IsMacroShaped(token))
                    continue; // not a placeholder; the next '%' may still open one

                // Both delimiters belong to this token, so scanning resumes past the closing one. Without
                // that, "%A_B%%C_D%" would read a second, imaginary token out of the two adjacent signs.
                i = close;

                if (knownMacros.Contains(token)) continue;
                if (seen.Add(token)) found.Add(token);
            }

            return found;
        }

        private static bool IsMacroShaped(string token)
        {
            if (token.Length < 2) return false;
            var hasUnderscore = false;
            foreach (var c in token)
            {
                if (c == '_') { hasUnderscore = true; continue; }
                if (!char.IsLetterOrDigit(c)) return false;
                if (c > 127) return false; // ASCII only; a macro name is not localised
            }
            return hasUnderscore;
        }
    }
}
