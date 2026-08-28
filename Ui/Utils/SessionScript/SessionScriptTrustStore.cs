using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Shawn.Utils;

namespace _1RM.Utils.SessionScript
{
    /// <summary>Which of the two script fields a command came out of. Only affects the wording of the prompt.</summary>
    public enum EScriptKind
    {
        BeforeConnect,
        AfterDisconnect,
    }

    /// <summary>
    /// Remembers which <c>CommandBeforeConnected</c> / <c>CommandAfterDisconnected</c> scripts the user has
    /// agreed to run on this machine.
    ///
    /// Those two fields are command lines that <c>ProtocolBase.RunScriptBeforeConnect</c> and
    /// <c>RunScriptAfterDisconnected</c> execute with the user's account on every connect and every
    /// disconnect — and with <c>HideCommandBeforeConnectedWindow</c>, with no window to notice. They are
    /// ordinary columns of the server list, so anything able to write that list can put a command on this
    /// desktop: a SQLite file on a network share, a MySQL or PostgreSQL source another admin maintains, a
    /// restored backup, a synced profile.
    ///
    /// The import round closed the "here is our server list, please import it" half of that. This closes the
    /// other half, which is the one with no import step at all: an operator opens the shared data source
    /// they open every morning and the script runs. It is the same threat <see
    /// cref="ExternalSecret.ExternalSecretTrustStore"/> exists for, and it is deliberately the same shape —
    /// approval once per exact command string, salted with this machine and account so a store that travels
    /// approves nothing where it lands.
    ///
    /// There is no allow-list of "safe" programs. A before-connect script is usually <c>cmd</c>,
    /// <c>powershell</c> or a <c>.bat</c>, all of which run anything, so trusting the executable rather than
    /// the whole line would trust everything.
    ///
    /// Nothing here reaches WPF or the IoC container: the prompt arrives as a delegate and the store path as
    /// a <see cref="Func{TResult}"/>, both wired from <c>Bootstrapper</c>. That is what lets the rules below
    /// be exercised without a window.
    /// </summary>
    public static class SessionScriptTrustStore
    {
        /// <summary>Asks the user about one command. True means run it.</summary>
        public delegate bool ConfirmDelegate(string command, EScriptKind kind);

        /// <summary>
        /// Wired at start-up to the confirmation dialog.
        ///
        /// Null refuses. A gate nobody connected would otherwise wave every command through while looking
        /// like a check; a script that does not run is a far cheaper failure than one that runs unasked.
        /// </summary>
        public static ConfirmDelegate? Confirm { get; set; }

        /// <summary>
        /// Where the approvals are kept. A delegate rather than a path because <c>AppPathHelper</c> reaches
        /// WPF and this file must not. Null keeps approvals in memory for the run and nothing on disk.
        /// </summary>
        public static Func<string>? StorePathProvider { get; set; }

        /// <summary>
        /// Set by <c>Tests/TestInit.cs</c>. Tests that exercise a protocol have nobody to answer a prompt,
        /// and the cases that cover the gate itself turn this back off for their own duration.
        /// </summary>
        public static bool AutoApproveForTests { get; set; } = false;

        /// <summary>Approved command hashes, mapped to the command they stand for so the file can be audited.</summary>
        private static Dictionary<string, string> _approved = new Dictionary<string, string>();
        private static readonly object Lock = new object();
        private static bool _loaded;

        /// <summary>
        /// Commands the user said no to, remembered for the rest of the run. A server whose disconnect
        /// script was refused would otherwise ask again every time the session closes.
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> DeclinedThisRun = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        /// <summary>
        /// Salted with the machine and the account, so approvals in a file that was synced, copied or
        /// restored from somewhere else do not apply here.
        /// </summary>
        private static string KeyOf(string command)
        {
            var material = $"{Environment.MachineName}|{Environment.UserName}|{command}";
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(material))).TrimEnd('=');
        }

        public static bool IsApproved(string? command)
        {
            if (AutoApproveForTests) return true;
            if (string.IsNullOrWhiteSpace(command)) return false;
            Load();
            lock (Lock)
            {
                return _approved.ContainsKey(KeyOf(command!));
            }
        }

        /// <summary>
        /// Records approval without asking, for the two places where the click itself is the consent: the
        /// editor's "test script" button, and saving a server whose script the user is looking at.
        /// </summary>
        public static void Approve(string? command)
        {
            if (AutoApproveForTests) return;
            if (string.IsNullOrWhiteSpace(command)) return;
            Load();
            lock (Lock)
            {
                _approved[KeyOf(command!)] = command!;
            }
            DeclinedThisRun.TryRemove(command!, out _);
            Save();
            SimpleLogHelper.Info("SessionScriptTrustStore: approved a session script");
        }

        /// <summary>
        /// The gate on the connect and disconnect paths. Returns true when the command may run, asking once
        /// if this exact line has not been seen on this machine before.
        /// </summary>
        public static bool EnsureApproved(string? command, EScriptKind kind)
        {
            if (AutoApproveForTests) return true;
            // Nothing to run is not a refusal: the callers already treat a blank field as "no script", and
            // answering false here would turn every server without a script into a failed connect.
            if (string.IsNullOrWhiteSpace(command)) return true;
            if (IsApproved(command)) return true;
            if (DeclinedThisRun.ContainsKey(command!)) return false;

            var confirm = Confirm;
            if (confirm == null)
            {
                SimpleLogHelper.Error("SessionScriptTrustStore: no prompt is wired, refusing to run a session script");
                return false;
            }

            bool agreed;
            try
            {
                agreed = confirm(command!, kind);
            }
            catch (Exception e)
            {
                // RunScriptAfterDisconnected is called from Process.Exited, which is a thread-pool thread
                // with no handler above it. A throwing dialog there must not end the process.
                SimpleLogHelper.Error($"SessionScriptTrustStore: the prompt failed, refusing. {e.Message}");
                return false;
            }

            if (!agreed)
            {
                DeclinedThisRun.TryAdd(command!, 0);
                SimpleLogHelper.Warning("SessionScriptTrustStore: the user refused a session script");
                return false;
            }

            Approve(command);
            return true;
        }

        /// <summary>Drops everything, including what is on disk. Test support only.</summary>
        public static void ResetForTests()
        {
            lock (Lock)
            {
                _approved = new Dictionary<string, string>();
                _loaded = false;
            }
            DeclinedThisRun.Clear();
        }

        private static void Load()
        {
            lock (Lock)
            {
                if (_loaded) return;
                _loaded = true;
                var path = PathOrNull();
                if (path == null) return;
                try
                {
                    if (!File.Exists(path)) return;
                    _approved = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8))
                                ?? new Dictionary<string, string>();
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Warning($"SessionScriptTrustStore: cannot read the approval store, {e.Message}");
                }
            }
        }

        private static void Save()
        {
            var path = PathOrNull();
            if (path == null) return;
            try
            {
                var dir = new FileInfo(path).Directory;
                if (dir?.Exists == false)
                    dir.Create();

                string json;
                lock (Lock)
                {
                    json = JsonConvert.SerializeObject(_approved, Formatting.Indented);
                }
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error($"SessionScriptTrustStore: cannot write the approval store, {e.Message}");
            }
        }

        /// <summary>
        /// The store path, or null when there is nowhere to keep it. A provider that throws is treated as
        /// "nowhere": losing persistence costs one extra prompt next run, an exception on the connect path
        /// costs the session.
        /// </summary>
        private static string? PathOrNull()
        {
            var provider = StorePathProvider;
            if (provider == null) return null;
            try
            {
                var path = provider();
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"SessionScriptTrustStore: cannot resolve the store path, {e.Message}");
                return null;
            }
        }
    }
}
