using System;
using Newtonsoft.Json;

namespace _1RM.Service.Audit
{
    /// <summary>Ways a credential, or the list holding them, can leave this app.</summary>
    public enum ESecretAccessEvent
    {
        /// <summary>A stored password was put on the clipboard.</summary>
        PasswordCopied,

        /// <summary>Selected servers were written to a JSON file with every password in cleartext.</summary>
        ServerListExported,

        /// <summary>A .rdp file was written for one server; it carries the password as a DPAPI blob.</summary>
        RdpFileExported,

        /// <summary>The whole configuration, credential database included, was packed into an archive.</summary>
        BackupCreated,

        /// <summary>The audit trail itself was exported. "Who pulled the log" is an audit question too.</summary>
        AuditLogExported,
    }

    /// <summary>
    /// One line in the credential-access log: who took a secret out of the app, when, which one, and where
    /// it went.
    ///
    /// Deliberately a different shape from <see cref="ConnectionAuditRecord"/> rather than four more values
    /// of <see cref="EAuditEvent"/>. A connection record is about one host and how long the session lasted;
    /// half of these events are about no host at all — "every password in the list, to this file" — and
    /// squeezing a destination path and a server count into <c>Reason</c> and <c>DurationSeconds</c> would
    /// produce a log nobody could read back.
    ///
    /// It holds no secret itself, for the same reason the connection record does not: an audit file is the
    /// one thing in this app most likely to be handed to somebody else. The destination is a path, never a
    /// copy of what was written there.
    /// </summary>
    public sealed class SecretAccessRecord : IAuditRecord
    {
        /// <summary>UTC, so records from machines in different time zones sort and merge correctly.</summary>
        [JsonProperty("t")]
        public DateTime TimeUtc { get; set; } = DateTime.UtcNow;

        [JsonProperty("e")]
        public ESecretAccessEvent Event { get; set; }

        /// <summary>The Windows account that operated the app, and on which machine.</summary>
        [JsonProperty("by")]
        public string LocalUser { get; set; } = "";

        [JsonProperty("host")]
        public string LocalMachine { get; set; } = "";

        /// <summary>The server's stable id for a single-server event, empty for one that covers a selection.</summary>
        [JsonProperty("sid")]
        public string ServerId { get; set; } = "";

        [JsonProperty("name")]
        public string ServerName { get; set; } = "";

        /// <summary>Protocol name as the app knows it: RDP, SSH, VNC, SFTP...</summary>
        [JsonProperty("proto")]
        public string Protocol { get; set; } = "";

        [JsonProperty("addr")]
        public string Address { get; set; } = "";

        /// <summary>The account the credential belongs to on the remote host. Never a password.</summary>
        [JsonProperty("user")]
        public string RemoteUser { get; set; } = "";

        /// <summary>Which data source the server came from — a shared database is worth telling apart.</summary>
        [JsonProperty("src")]
        public string DataSource { get; set; } = "";

        /// <summary>How many servers the event covered. One for a single-server event.</summary>
        [JsonProperty("n")]
        public int Count { get; set; } = 1;

        /// <summary>
        /// Where it went: a file path, or <see cref="DESTINATION_CLIPBOARD"/>. A path rather than a name,
        /// because "which share did the export land on" is most of the question.
        /// </summary>
        [JsonProperty("dest")]
        public string Destination { get; set; } = "";

        /// <summary>Anything else worth knowing about the event. Empty for most of them.</summary>
        [JsonProperty("note")]
        public string Note { get; set; } = "";

        public const string DESTINATION_CLIPBOARD = "clipboard";
    }
}
