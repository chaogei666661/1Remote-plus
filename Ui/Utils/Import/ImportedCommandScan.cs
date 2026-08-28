using System;
using System.Collections.Generic;
using System.Text;
using _1RM.Utils.FileTransmit;

namespace _1RM.Utils.Import
{
    /// <summary>Which field of an imported entry would run something.</summary>
    public enum EImportedCommandKind
    {
        /// <summary><c>ProtocolBase.CommandBeforeConnected</c>: runs every time the entry is opened.</summary>
        BeforeConnect,

        /// <summary><c>ProtocolBase.CommandAfterDisconnected</c>: runs every time the session ends.</summary>
        AfterDisconnect,

        /// <summary><c>LocalApp.ExePath</c>: the entry *is* a program, and opening it starts that program.</summary>
        LocalApp,
    }

    /// <summary>
    /// The command-carrying fields of one entry in a file being imported.
    ///
    /// A flat record rather than a <c>ProtocolBase</c> so the rules below can be checked without standing
    /// up the app: <c>ProtocolBase</c> reaches WPF imaging, the IoC container and the data sources. The
    /// four importers build these from the servers they parsed.
    /// </summary>
    public sealed class ImportedCommandSource
    {
        public ImportedCommandSource(string? serverName,
                                     string? commandBeforeConnected = null,
                                     string? commandAfterDisconnected = null,
                                     string? localAppPath = null)
        {
            ServerName = serverName ?? "";
            CommandBeforeConnected = commandBeforeConnected ?? "";
            CommandAfterDisconnected = commandAfterDisconnected ?? "";
            LocalAppPath = localAppPath ?? "";
        }

        public string ServerName { get; }

        public string CommandBeforeConnected { get; }

        public string CommandAfterDisconnected { get; }

        /// <summary>Empty unless the entry is a <c>LocalApp</c>.</summary>
        public string LocalAppPath { get; }
    }

    /// <summary>One thing an imported entry would run on this machine.</summary>
    public sealed class ImportedCommand
    {
        public ImportedCommand(int entryIndex, string serverName, EImportedCommandKind kind, string commandLine)
        {
            EntryIndex = entryIndex;
            ServerName = serverName;
            Kind = kind;
            CommandLine = commandLine;
        }

        /// <summary>
        /// Which entry of the import this came from. Counted rather than the name, because a file is free
        /// to give two servers the same name — deliberately, even, to make three findings look like one.
        /// </summary>
        public int EntryIndex { get; }

        public string ServerName { get; }

        public EImportedCommandKind Kind { get; }

        /// <summary>Already shortened and stripped of invisible characters — see <see cref="ImportedCommandScan"/>.</summary>
        public string CommandLine { get; }
    }

    /// <summary>
    /// Finds the entries in an import that would run a program on the machine doing the importing.
    ///
    /// A server entry is not only an address. Three of its fields are command lines that this app executes
    /// locally, with the user's own privileges and no elevation prompt:
    ///
    /// <list type="bullet">
    /// <item><c>CommandBeforeConnected</c> — run by <c>RunScriptBeforeConnect</c> on every connect, and
    /// with <c>HideCommandBeforeConnectedWindow</c> set it runs with no window at all.</item>
    /// <item><c>CommandAfterDisconnected</c> — the same, when the session ends.</item>
    /// <item>A <c>LocalApp</c> entry's <c>ExePath</c> and arguments, which is the whole point of that
    /// protocol.</item>
    /// </list>
    ///
    /// All three travel inside the JSON, the PRemoteM/1Remote database and the backup archive, and the
    /// importers inserted them without showing them. So "here is my server list" — a file, a shared SQLite
    /// on a network drive, a forum attachment — was a way to put a command on somebody's machine that runs
    /// the next time they open the entry, which they will, because that is why they imported it.
    ///
    /// This is the same threat the <c>cmd://</c> external-secret gate exists for, and that gate's own
    /// reasoning says so: an approval to execute something is about this machine and must not travel with
    /// a shared database. The pre/post-connect scripts had no such gate. This does not add one at connect
    /// time — it makes the import say what it is bringing in, so the answer is given by somebody who knows
    /// where the file came from.
    ///
    /// Nothing here is rewritten or refused. The scan reports; the caller asks.
    /// </summary>
    public static class ImportedCommandScan
    {
        /// <summary>How many commands <see cref="Describe"/> spells out before it starts counting.</summary>
        public const int DefaultLimit = 5;

        /// <summary>
        /// How much of one command line is shown. Long enough to recognise a legitimate script by, short
        /// enough that a file cannot push the question off the bottom of the dialog with one entry.
        /// </summary>
        public const int MaxCommandLength = 120;

        /// <summary>
        /// Every command the entries in <paramref name="sources"/> would run, in the order they were given
        /// and, within one entry, before-connect first.
        /// </summary>
        public static IReadOnlyList<ImportedCommand> Scan(IEnumerable<ImportedCommandSource>? sources)
        {
            var found = new List<ImportedCommand>();
            if (sources == null)
                return found;

            var index = 0;
            foreach (var source in sources)
            {
                var at = index++;
                if (source == null)
                    continue;
                Add(found, at, source, EImportedCommandKind.BeforeConnect, source.CommandBeforeConnected);
                Add(found, at, source, EImportedCommandKind.AfterDisconnect, source.CommandAfterDisconnected);
                Add(found, at, source, EImportedCommandKind.LocalApp, source.LocalAppPath);
            }
            return found;
        }

        private static void Add(List<ImportedCommand> found, int entryIndex, ImportedCommandSource source, EImportedCommandKind kind, string value)
        {
            // Whitespace-only is what RunScriptBeforeConnect itself treats as "no script", so a file full of
            // blank fields must not produce a warning with nothing in it.
            if (string.IsNullOrWhiteSpace(value))
                return;
            found.Add(new ImportedCommand(entryIndex, NameFor(source.ServerName), kind, Present(value)));
        }

        private static string NameFor(string serverName)
        {
            var display = RemoteNameInspector.ToDisplayText(serverName).Trim();
            return display.Length == 0 ? "?" : Shorten(display, 40);
        }

        /// <summary>
        /// The command as it will be shown.
        ///
        /// Invisible characters are spelled out rather than obeyed, for the reason
        /// <see cref="RemoteNameInspector"/> exists: a newline would let one entry push the rest of the
        /// list and the question itself out of the dialog, and a right-to-left override would let a command
        /// be drawn as something other than what runs. Then it is cut to a length a dialog can hold.
        /// </summary>
        private static string Present(string command)
        {
            return Shorten(RemoteNameInspector.ToDisplayText(command).Trim(), MaxCommandLength);
        }

        private static string Shorten(string text, int max)
        {
            // The head is what is kept here, unlike a path: the program being run is at the front of a
            // command line and the arguments after it matter less for deciding whether to say yes.
            return text.Length <= max ? text : text.Substring(0, max - 3) + "...";
        }

        /// <summary>
        /// The body of the confirmation: one line per command, up to <paramref name="limit"/> of them,
        /// then a line for the rest.
        /// </summary>
        /// <param name="describeKind">
        /// The localised name of a field ("before connect", "after disconnect", "local application"). The
        /// caller owns it because this class is deliberately free of the translation service. Null falls
        /// back to the enum name.
        /// </param>
        /// <param name="describeOmitted">
        /// Given the number of lines not spelled out, the localised text that says so. Null falls back to
        /// <c>(+n)</c>.
        /// </param>
        public static string Describe(IReadOnlyList<ImportedCommand>? found,
                                      int limit = DefaultLimit,
                                      Func<EImportedCommandKind, string>? describeKind = null,
                                      Func<int, string>? describeOmitted = null)
        {
            if (found == null || found.Count == 0)
                return "";
            if (limit < 1)
                limit = 1;

            var builder = new StringBuilder();
            var shown = Math.Min(limit, found.Count);
            for (var i = 0; i < shown; i++)
            {
                var item = found[i];
                var kind = describeKind == null ? item.Kind.ToString() : describeKind(item.Kind);
                if (builder.Length > 0)
                    builder.Append(Environment.NewLine);
                builder.Append("• ").Append(item.ServerName).Append(" [").Append(kind).Append("]  ").Append(item.CommandLine);
            }

            var omitted = found.Count - shown;
            if (omitted > 0)
            {
                var tail = describeOmitted == null ? $"(+{omitted})" : describeOmitted(omitted);
                if (!string.IsNullOrWhiteSpace(tail))
                    builder.Append(Environment.NewLine).Append(tail);
            }

            return builder.ToString();
        }

        /// <summary>
        /// How many distinct entries carry at least one command. The message leads with this, because "3 of
        /// the 40 servers in this file" is the number that decides whether the import looks normal, and it
        /// is not the same as the number of command lines.
        /// </summary>
        public static int ServerCount(IReadOnlyList<ImportedCommand>? found)
        {
            if (found == null || found.Count == 0)
                return 0;
            var entries = new HashSet<int>();
            foreach (var item in found)
                entries.Add(item.EntryIndex);
            return entries.Count;
        }
    }
}
