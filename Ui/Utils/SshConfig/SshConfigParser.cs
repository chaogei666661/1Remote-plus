using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace _1RM.Utils.SshConfig
{
    /// <summary>
    /// One connectable <c>Host</c> block from an OpenSSH client config.
    /// </summary>
    public sealed class SshConfigEntry
    {
        /// <summary>The name after <c>Host</c>, which is what the user types after <c>ssh</c>.</summary>
        public string Alias { get; set; } = "";

        /// <summary>The <c>HostName</c>, or the alias when the block does not override it.</summary>
        public string HostName { get; set; } = "";

        public string User { get; set; } = "";
        public int Port { get; set; } = 22;
        public string IdentityFile { get; set; } = "";

        /// <summary>The single <c>ProxyJump</c> hop, when there is exactly one. Empty otherwise.</summary>
        public string ProxyJump { get; set; } = "";
    }

    /// <summary>
    /// Reads the subset of <c>~/.ssh/config</c> that maps onto a stored connection.
    ///
    /// What it reproduces of OpenSSH's own reading:
    ///
    /// <list type="bullet">
    /// <item><c>Include</c>, with glob patterns, <c>~</c> and paths relative to the directory the top-level
    /// config sits in — which for <c>~/.ssh/config</c> is the <c>~/.ssh</c> that ssh_config(5) specifies.
    /// A file split across <c>~/.ssh/config.d/*</c> is a normal layout, and before this it imported as
    /// nothing at all.</item>
    /// <item>Pattern blocks as defaults. <c>Host *</c> carrying <c>User deploy</c> is not a machine, but it
    /// does decide who <c>ssh anything</c> logs in as, and dropping it meant importing servers with the
    /// wrong account.</item>
    /// <item>First value wins, across the whole file rather than only within one block — that is what ssh
    /// does, and it is why a <c>Host *</c> block at the <em>top</em> of a file overrides everything below
    /// it while the same block at the bottom overrides nothing.</item>
    /// <item><c>Match</c> sections whose criteria can be decided from the file alone: <c>all</c>,
    /// <c>host</c> and <c>originalhost</c>, including negated forms.</item>
    /// </list>
    ///
    /// Still deliberately not implemented: <c>Match exec</c>, <c>user</c>, <c>localuser</c>,
    /// <c>localnetwork</c>, <c>tagged</c>, <c>command</c>, <c>version</c>, <c>canonical</c> and
    /// <c>final</c>. The first would run a command, and none of the rest can be answered by a program that
    /// is filling in an import dialog rather than opening a connection. Such a section is skipped whole,
    /// so a criterion we cannot evaluate never contributes a setting we would have to guess at.
    /// </summary>
    public static class SshConfigParser
    {
        /// <summary>
        /// How far <c>Include</c> may nest. OpenSSH's own limit is 16; a deeper file is far more likely to
        /// be a loop than a layout.
        /// </summary>
        public const int MAX_INCLUDE_DEPTH = 16;

        public static string DefaultConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

        /// <summary>
        /// Reads lines that are already in hand. <c>Include</c> is ignored here because there is no
        /// directory to resolve it against; use <see cref="ParseFile"/> for a config on disk.
        /// </summary>
        public static List<SshConfigEntry> Parse(IEnumerable<string> lines)
        {
            var sections = new List<Section>();
            ReadInto(sections, lines, baseDirectory: null, depth: 0, visited: new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            return Build(sections);
        }

        public static List<SshConfigEntry> ParseFile(string path)
        {
            var full = Path.GetFullPath(path);
            var baseDirectory = Path.GetDirectoryName(full) ?? "";
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { full };

            var sections = new List<Section>();
            ReadInto(sections, File.ReadAllLines(full), baseDirectory, depth: 0, visited: visited);
            return Build(sections);
        }

        #region reading

        /// <summary>
        /// A run of settings and the condition that decides which hosts they apply to. Both <c>Host</c> and
        /// <c>Match</c> start one, which is what makes "first value wins" a property of the file rather
        /// than of a block.
        /// </summary>
        private sealed class Section
        {
            /// <summary>Patterns the name has to match at least one of. Empty means "no positive test".</summary>
            public readonly List<string> Positive = new List<string>();

            /// <summary>Patterns that veto the section when the name matches any of them.</summary>
            public readonly List<string> Negative = new List<string>();

            /// <summary>True for <c>Host *</c> and <c>Match all</c>: nothing left to test.</summary>
            public bool MatchesEverything;

            /// <summary>A <c>Match</c> criterion we cannot decide. The whole section is inert.</summary>
            public bool Unevaluable;

            /// <summary>
            /// A <c>Host</c> section, whose plain patterns are the aliases the user can connect to. A
            /// <c>Match</c> section declares no host of its own, however concrete its patterns look.
            /// </summary>
            public bool DeclaresHosts;

            /// <summary>False for the <c>host</c> criterion, which ssh tests against the target hostname.</summary>
            public bool MatchesAlias = true;

            /// <summary>
            /// True for a <c>Host</c> line, whose pattern list needs a positive hit — <c>Host !jump</c>
            /// names nothing. False for <c>Match</c>, where <c>!host x</c> is a criterion that is simply
            /// satisfied by every host but <c>x</c>.
            /// </summary>
            public bool RequiresPositive = true;

            public readonly List<KeyValuePair<string, string>> Settings = new List<KeyValuePair<string, string>>();
        }

        private static void ReadInto(List<Section> sections, IEnumerable<string> lines, string? baseDirectory, int depth, HashSet<string> visited)
        {
            Section? current = null;

            foreach (var raw in lines)
            {
                if (!TrySplit(raw, out var keyword, out var value)) continue;

                if (string.Equals(keyword, "Host", StringComparison.OrdinalIgnoreCase))
                {
                    current = StartHostSection(value);
                    sections.Add(current);
                    continue;
                }

                if (string.Equals(keyword, "Match", StringComparison.OrdinalIgnoreCase))
                {
                    current = StartMatchSection(value);
                    sections.Add(current);
                    continue;
                }

                if (string.Equals(keyword, "Include", StringComparison.OrdinalIgnoreCase))
                {
                    // ssh splices the included lines in where the directive sits, so a Host block opened
                    // before the Include stays open across it and the settings that follow keep their
                    // place in the first-value-wins order. Reading into the same list, in order, is that.
                    if (baseDirectory != null && depth < MAX_INCLUDE_DEPTH)
                    {
                        foreach (var included in ResolveInclude(value, baseDirectory, visited))
                        {
                            string[] includedLines;
                            try
                            {
                                includedLines = File.ReadAllLines(included);
                            }
                            catch
                            {
                                // Unreadable is the same as absent for our purposes: import what is legible.
                                continue;
                            }
                            ReadInto(sections, includedLines, baseDirectory, depth + 1, visited);
                        }
                        // Anything after the Include belongs to the block that was open before it.
                        if (current != null) sections.Add(current = Continue(current));
                    }
                    continue;
                }

                current?.Settings.Add(new KeyValuePair<string, string>(keyword, value));
            }
        }

        /// <summary>A copy of a section's condition with no settings, to resume a block after an Include.</summary>
        private static Section Continue(Section section)
        {
            var resumed = new Section
            {
                MatchesEverything = section.MatchesEverything,
                Unevaluable = section.Unevaluable,
                MatchesAlias = section.MatchesAlias,
                RequiresPositive = section.RequiresPositive,
                // The aliases were already declared by the original section; declaring them twice would
                // not add an entry (they are deduplicated) but it would be a lie about where they came from.
                DeclaresHosts = false,
            };
            resumed.Positive.AddRange(section.Positive);
            resumed.Negative.AddRange(section.Negative);
            return resumed;
        }

        private static Section StartHostSection(string patterns)
        {
            var section = new Section { DeclaresHosts = true };
            foreach (var token in SplitTokens(patterns))
                AddPattern(section, token);
            return section;
        }

        private static Section StartMatchSection(string criteria)
        {
            var section = new Section { RequiresPositive = false };
            var tokens = SplitTokens(criteria);

            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var negated = token.StartsWith("!", StringComparison.Ordinal);
                var name = negated ? token.Substring(1) : token;

                if (string.Equals(name, "all", StringComparison.OrdinalIgnoreCase))
                {
                    // "all" is only legal alone or after canonical/final, both of which we already refuse.
                    if (negated) { section.Unevaluable = true; return section; }
                    section.MatchesEverything = true;
                    continue;
                }

                var isHost = string.Equals(name, "host", StringComparison.OrdinalIgnoreCase);
                var isOriginalHost = string.Equals(name, "originalhost", StringComparison.OrdinalIgnoreCase);
                if (!isHost && !isOriginalHost)
                {
                    // exec, user, localuser, localnetwork, tagged, command, version, canonical, final.
                    section.Unevaluable = true;
                    return section;
                }

                if (i + 1 >= tokens.Count) { section.Unevaluable = true; return section; }
                var argument = tokens[++i];

                // Mixing the two would need one name tested against the alias and another against the
                // address in the same condition; that is a config we would rather skip than half-read.
                if (section.Positive.Count > 0 || section.Negative.Count > 0)
                {
                    if (section.MatchesAlias != isOriginalHost) { section.Unevaluable = true; return section; }
                }
                section.MatchesAlias = isOriginalHost;

                foreach (var pattern in argument.Split(','))
                {
                    var trimmed = pattern.Trim();
                    if (trimmed.Length == 0) continue;
                    if (negated) section.Negative.Add(trimmed);
                    else section.Positive.Add(trimmed);
                }
            }

            if (!section.MatchesEverything && section.Positive.Count == 0 && section.Negative.Count == 0)
                section.Unevaluable = true;

            return section;
        }

        private static void AddPattern(Section section, string token)
        {
            var pattern = Unquote(token);
            if (pattern.StartsWith("!", StringComparison.Ordinal))
            {
                var negated = pattern.Substring(1);
                if (negated.Length > 0) section.Negative.Add(negated);
                return;
            }
            if (pattern.Length == 0) return;
            if (pattern == "*") section.MatchesEverything = true;
            section.Positive.Add(pattern);
        }

        /// <summary>
        /// Expands one <c>Include</c> value into the files it names, in the lexical order ssh_config(5)
        /// promises. A file already read is dropped, which is what stops two snippets that include each
        /// other from filling memory.
        /// </summary>
        private static IEnumerable<string> ResolveInclude(string value, string baseDirectory, HashSet<string> visited)
        {
            var found = new List<string>();

            foreach (var token in SplitTokens(value))
            {
                var pattern = ExpandHome(Unquote(token));
                if (pattern.Length == 0) continue;

                if (!Path.IsPathRooted(pattern))
                    pattern = Path.Combine(baseDirectory, pattern);

                var directory = Path.GetDirectoryName(pattern) ?? baseDirectory;
                var leaf = Path.GetFileName(pattern);
                if (leaf.Length == 0) continue;

                var matches = new List<string>();
                try
                {
                    if (leaf.IndexOfAny(new[] { '*', '?' }) >= 0)
                    {
                        if (Directory.Exists(directory))
                            matches.AddRange(Directory.GetFiles(directory, leaf));
                    }
                    else if (File.Exists(pattern))
                    {
                        matches.Add(pattern);
                    }
                }
                catch
                {
                    // A directory we are not allowed to list contributes nothing, same as an absent one.
                    continue;
                }

                matches.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var match in matches)
                {
                    string full;
                    try { full = Path.GetFullPath(match); }
                    catch { continue; }
                    if (visited.Add(full)) found.Add(full);
                }
            }

            return found;
        }

        #endregion

        #region building

        private static List<SshConfigEntry> Build(List<Section> sections)
        {
            var entries = new List<SshConfigEntry>();
            var byAlias = new Dictionary<string, SshConfigEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var section in sections)
            {
                if (!section.DeclaresHosts || section.Unevaluable) continue;
                foreach (var pattern in section.Positive)
                {
                    // A pattern is a filter over names, not a name: there is no machine called "web*".
                    if (pattern.IndexOfAny(new[] { '*', '?' }) >= 0) continue;
                    if (byAlias.ContainsKey(pattern)) continue;

                    var entry = new SshConfigEntry { Alias = pattern };
                    byAlias.Add(pattern, entry);
                    entries.Add(entry);
                }
            }

            foreach (var entry in entries)
            {
                var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var section in sections)
                {
                    if (section.Unevaluable) continue;
                    if (!Matches(section, entry)) continue;

                    foreach (var setting in section.Settings)
                    {
                        if (!assigned.Add(setting.Key)) continue;
                        Apply(entry, setting.Key, setting.Value);
                    }
                }

                // A block whose HostName was never given connects to the alias itself.
                if (entry.HostName.Length == 0) entry.HostName = entry.Alias;
            }

            return entries;
        }

        private static bool Matches(Section section, SshConfigEntry entry)
        {
            // ssh tests "host" against the address it is about to dial, which at this point in the file is
            // whatever HostName has already resolved to, and the alias until something sets one.
            var name = section.MatchesAlias
                ? entry.Alias
                : entry.HostName.Length > 0 ? entry.HostName : entry.Alias;

            foreach (var pattern in section.Negative)
                if (IsMatch(name, pattern))
                    return false;

            if (section.MatchesEverything) return true;
            if (section.Positive.Count == 0) return !section.RequiresPositive;

            foreach (var pattern in section.Positive)
                if (IsMatch(name, pattern))
                    return true;

            return false;
        }

        /// <summary>glob(7) as ssh uses it for host patterns: <c>*</c> and <c>?</c>, case insensitive.</summary>
        internal static bool IsMatch(string name, string pattern)
        {
            if (pattern.IndexOfAny(new[] { '*', '?' }) < 0)
                return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);

            // name[i] against pattern[j], with the last '*' remembered so a failure can back up to it.
            int i = 0, j = 0, star = -1, resume = 0;
            while (i < name.Length)
            {
                if (j < pattern.Length && (pattern[j] == '?' || char.ToLowerInvariant(pattern[j]) == char.ToLowerInvariant(name[i])))
                {
                    i++;
                    j++;
                }
                else if (j < pattern.Length && pattern[j] == '*')
                {
                    star = j++;
                    resume = i;
                }
                else if (star >= 0)
                {
                    j = star + 1;
                    i = ++resume;
                }
                else
                {
                    return false;
                }
            }

            while (j < pattern.Length && pattern[j] == '*') j++;
            return j == pattern.Length;
        }

        private static void Apply(SshConfigEntry entry, string keyword, string value)
        {
            switch (keyword.ToLowerInvariant())
            {
                case "hostname":
                    entry.HostName = ExpandTokens(Unquote(value), entry.Alias);
                    break;
                case "user":
                    entry.User = Unquote(value);
                    break;
                case "port":
                    if (int.TryParse(Unquote(value), out var port) && port > 0 && port <= 65535)
                        entry.Port = port;
                    break;
                case "identityfile":
                    // ssh collects every IdentityFile and offers them in turn; a stored connection holds
                    // one, so it gets the first, which is the one ssh would try first.
                    entry.IdentityFile = ExpandHome(ExpandTokens(Unquote(value), entry.Alias));
                    break;
                case "proxyjump":
                    var hops = Unquote(value).Split(',');
                    // Only a single hop maps onto one jump host; a chain would need tunnels through tunnels.
                    if (hops.Length == 1 && !string.Equals(hops[0].Trim(), "none", StringComparison.OrdinalIgnoreCase))
                        entry.ProxyJump = StripUserAndPort(hops[0].Trim());
                    break;
            }
        }

        #endregion

        #region lexing

        /// <summary>
        /// Splits "Keyword value", "Keyword=value" and any amount of leading whitespace. Returns false for
        /// blank lines and comments.
        /// </summary>
        private static bool TrySplit(string raw, out string keyword, out string value)
        {
            keyword = "";
            value = "";

            var line = (raw ?? "").Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) return false;

            var separator = line.IndexOfAny(new[] { ' ', '\t', '=' });
            if (separator <= 0) return false;

            keyword = line.Substring(0, separator);
            value = line.Substring(separator + 1).TrimStart(' ', '\t', '=').Trim();
            return value.Length > 0;
        }

        /// <summary>Whitespace-separated tokens, except inside double quotes — a path can contain a space.</summary>
        private static List<string> SplitTokens(string value)
        {
            var tokens = new List<string>();
            var token = new StringBuilder();
            var quoted = false;

            foreach (var c in value)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                    continue;
                }
                if (!quoted && (c == ' ' || c == '\t'))
                {
                    if (token.Length > 0) { tokens.Add(token.ToString()); token.Clear(); }
                    continue;
                }
                token.Append(c);
            }

            if (token.Length > 0) tokens.Add(token.ToString());
            return tokens;
        }

        private static string Unquote(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Length >= 2 && trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal))
                return trimmed.Substring(1, trimmed.Length - 2);
            return trimmed;
        }

        /// <summary>A ProxyJump hop may carry its own user and port; the alias is what names the block.</summary>
        private static string StripUserAndPort(string hop)
        {
            var at = hop.LastIndexOf('@');
            if (at >= 0) hop = hop.Substring(at + 1);
            var colon = hop.LastIndexOf(':');
            if (colon > 0) hop = hop.Substring(0, colon);
            return hop.Trim();
        }

        /// <summary>
        /// The two tokens that mean something without a live connection: <c>%h</c>, the host being
        /// connected to, and <c>%%</c>. The rest name things a connection has and an import does not.
        /// </summary>
        private static string ExpandTokens(string value, string alias)
        {
            if (value.IndexOf('%') < 0) return value;

            var expanded = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != '%' || i + 1 >= value.Length)
                {
                    expanded.Append(value[i]);
                    continue;
                }

                switch (value[i + 1])
                {
                    case 'h':
                        expanded.Append(alias);
                        i++;
                        break;
                    case '%':
                        expanded.Append('%');
                        i++;
                        break;
                    default:
                        expanded.Append(value[i]);
                        break;
                }
            }

            return expanded.ToString();
        }

        private static string ExpandHome(string path)
        {
            if (!path.StartsWith("~", StringComparison.Ordinal)) return path;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path.TrimStart('~').TrimStart('/', '\\'));
        }

        #endregion
    }
}
