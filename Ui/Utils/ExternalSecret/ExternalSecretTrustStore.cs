using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using _1RM.Service;
using Newtonsoft.Json;
using Shawn.Utils;

namespace _1RM.Utils.ExternalSecret
{
    /// <summary>
    /// Remembers which <c>cmd://</c> command lines the user has agreed to run, the same trust-on-first-use
    /// idea <see cref="HostTrustService"/> uses for host identities.
    ///
    /// The reason this has to exist: a stored password may be a command line, and it is executed at connect
    /// time. Anything able to write the server list — a SQLite file on a network share, a MySQL/PostgreSQL
    /// source somebody else administers, a restored <c>.1rbak</c>, an imported mRemoteNG file — could
    /// therefore run code on this machine without the user ever agreeing to it. Approval is asked for once
    /// per exact command string and nothing runs before it is given.
    ///
    /// Approvals are machine-local by construction: the file records a hash of the command salted with this
    /// machine and user, so a store copied or restored from elsewhere approves nothing here, while restoring
    /// your own backup onto the same machine keeps what you had already approved. There is deliberately no
    /// wildcard and no vendor allow-list — <c>bw</c> and <c>op</c> can be told to run anything, so trusting
    /// the executable rather than the whole line would trust nothing at all.
    /// </summary>
    public static class ExternalSecretTrustStore
    {
        public delegate bool ConfirmDelegate(string title, string message);

        /// <summary>Replaced in tests; by default this is the normal confirmation dialog.</summary>
        public static ConfirmDelegate Confirm { get; set; } =
            (title, message) => MessageBoxHelper.Confirm(message, title: title);

        /// <summary>
        /// Set by <c>Tests/TestInit.cs</c>. The existing resolver tests shell out for real, and there is no
        /// user to answer a prompt in a test run, so they opt out of the gate rather than each having to
        /// pre-approve their command line.
        /// </summary>
        public static bool AutoApproveForTests { get; set; } = false;

        /// <summary>Lets a test point the store at a temp directory instead of the real locality folder.</summary>
        public static string? StorePathOverride { get; set; }

        /// <summary>Approved command hashes, mapped to the command they stand for so the file can be audited.</summary>
        private static Dictionary<string, string> _approved = new Dictionary<string, string>();
        private static readonly object Lock = new object();
        private static bool _loaded;

        /// <summary>
        /// Commands the user said no to, remembered for the rest of the run. Without this a server with a
        /// password and a key passphrase would ask twice about the same refusal before failing to connect.
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> DeclinedThisRun = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        private static string StorePath => StorePathOverride ?? AppPathHelper.Instance.ExternalSecretTrustJsonPath;

        /// <summary>
        /// Salted with the machine and user so the approvals in a restored or synced file do not apply
        /// anywhere but where they were given.
        /// </summary>
        private static string KeyOf(string command)
        {
            var material = $"{Environment.MachineName}|{Environment.UserName}|{command}";
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(material))).TrimEnd('=');
        }

        public static bool IsApproved(string command)
        {
            if (AutoApproveForTests) return true;
            if (string.IsNullOrWhiteSpace(command)) return false;
            Load();
            lock (Lock)
            {
                return _approved.ContainsKey(KeyOf(command));
            }
        }

        /// <summary>
        /// Records approval without asking. For the editor's test button, where running the command *is*
        /// what the user just clicked.
        /// </summary>
        public static void Approve(string command)
        {
            if (AutoApproveForTests) return;
            if (string.IsNullOrWhiteSpace(command)) return;
            Load();
            lock (Lock)
            {
                _approved[KeyOf(command)] = command;
            }
            DeclinedThisRun.TryRemove(command, out _);
            Save();
            SimpleLogHelper.Info($"ExternalSecretTrustStore: approved '{command}'");
        }

        /// <summary>
        /// The gate on the connect path. Returns true when the command may run, asking the user once if this
        /// is the first time it has been seen on this machine. A refusal is remembered for the rest of the
        /// run so one declined server does not produce a dialog per secret field.
        /// </summary>
        public static bool EnsureApproved(string command)
        {
            if (AutoApproveForTests) return true;
            if (string.IsNullOrWhiteSpace(command)) return false;
            if (IsApproved(command)) return true;
            if (DeclinedThisRun.ContainsKey(command)) return false;

            var message = IoC.Translate("external_secret_trust_new", command);
            if (!Confirm(IoC.Translate("external_secret_trust_title"), message))
            {
                DeclinedThisRun.TryAdd(command, 0);
                SimpleLogHelper.Warning($"ExternalSecretTrustStore: user refused to run '{command}'");
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
                try
                {
                    var path = StorePath;
                    if (!File.Exists(path)) return;
                    _approved = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8))
                                ?? new Dictionary<string, string>();
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Warning($"ExternalSecretTrustStore: cannot read the approval store, {e.Message}");
                }
            }
        }

        private static void Save()
        {
            try
            {
                var path = StorePath;
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
                SimpleLogHelper.Error($"ExternalSecretTrustStore: cannot write the approval store, {e.Message}");
            }
        }
    }
}
