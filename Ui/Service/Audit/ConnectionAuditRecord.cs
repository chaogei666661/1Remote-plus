using System;
using Newtonsoft.Json;

namespace _1RM.Service.Audit
{
    /// <summary>What happened to a session, from the app's point of view.</summary>
    public enum EAuditEvent
    {
        /// <summary>The user asked for a session. Always the first line for an attempt.</summary>
        ConnectStarted,

        /// <summary>A session was established and handed to a host.</summary>
        SessionOpened,

        /// <summary>The attempt ended without a session. <see cref="ConnectionAuditRecord.Reason"/> says why.</summary>
        ConnectFailed,

        /// <summary>A session that had been opened ended.</summary>
        SessionClosed,
    }

    /// <summary>
    /// One line in the audit log: who, when, to which host, and how it ended.
    ///
    /// Deliberately not a snapshot of the server: no password, no private key, no key passphrase, no
    /// external-secret command line. An audit record is the one file in this app that a user is most likely
    /// to hand to somebody else — a ticket, an auditor, a compliance review — so it holds only what an
    /// access report needs and nothing that would be a leak if it were forwarded.
    /// </summary>
    public sealed class ConnectionAuditRecord : IAuditRecord
    {
        /// <summary>UTC, so records from machines in different time zones sort and merge correctly.</summary>
        [JsonProperty("t")]
        public DateTime TimeUtc { get; set; } = DateTime.UtcNow;

        [JsonProperty("e")]
        public EAuditEvent Event { get; set; }

        /// <summary>Protocol name as the app knows it: RDP, SSH, VNC, SFTP...</summary>
        [JsonProperty("proto")]
        public string Protocol { get; set; } = "";

        /// <summary>The server's stable id, so entries survive a rename.</summary>
        [JsonProperty("sid")]
        public string ServerId { get; set; } = "";

        [JsonProperty("name")]
        public string ServerName { get; set; } = "";

        [JsonProperty("addr")]
        public string Address { get; set; } = "";

        [JsonProperty("port")]
        public int Port { get; set; }

        /// <summary>The account used on the remote host. Never a password.</summary>
        [JsonProperty("user")]
        public string RemoteUser { get; set; } = "";

        /// <summary>Which data source the server came from — a shared database is worth telling apart.</summary>
        [JsonProperty("src")]
        public string DataSource { get; set; } = "";

        /// <summary>The proxy or jump host the session went through, empty for a direct connection.</summary>
        [JsonProperty("proxy")]
        public string Proxy { get; set; } = "";

        /// <summary>Ties the four events of one attempt together.</summary>
        [JsonProperty("cid")]
        public string ConnectionId { get; set; } = "";

        /// <summary>
        /// For <see cref="EAuditEvent.ConnectFailed"/>, the
        /// <see cref="Utils.Diagnostics.EConnectionFailure"/> name; for a close, how it ended. Empty
        /// otherwise.
        /// </summary>
        [JsonProperty("reason")]
        public string Reason { get; set; } = "";

        /// <summary>The Windows account that operated the app, and on which machine.</summary>
        [JsonProperty("by")]
        public string LocalUser { get; set; } = "";

        [JsonProperty("host")]
        public string LocalMachine { get; set; } = "";

        /// <summary>Session length in seconds, on a close. Zero elsewhere.</summary>
        [JsonProperty("secs")]
        public long DurationSeconds { get; set; }
    }
}
