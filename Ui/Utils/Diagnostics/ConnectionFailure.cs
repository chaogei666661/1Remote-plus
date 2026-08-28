namespace _1RM.Utils.Diagnostics
{
    /// <summary>
    /// Why a connection attempt did not produce a session.
    ///
    /// The value is what decides whether the UI offers "retry" and which sentence it puts next to the raw
    /// error, so the categories are drawn along the line the user has to act on — not along the line the
    /// library that threw happens to draw.
    /// </summary>
    public enum EConnectionFailure
    {
        /// <summary>Nothing recognisable. The raw message is all there is.</summary>
        Unknown,

        /// <summary>The name does not resolve. Retrying the same name will not help until DNS does.</summary>
        NameResolution,

        /// <summary>The host answered the SYN with a reset: reachable, but nothing is listening on that port.</summary>
        Refused,

        /// <summary>Nothing answered at all inside the timeout — filtered, down, or the wrong address.</summary>
        Timeout,

        /// <summary>No route to the host, or the local machine has no usable network.</summary>
        NetworkUnreachable,

        /// <summary>The peer closed an established connection before the session was usable.</summary>
        ConnectionDropped,

        /// <summary>The host identity did not match what was accepted before, or the user declined it.</summary>
        HostIdentityRejected,

        /// <summary>TLS could not be established: bad chain, expired certificate, protocol mismatch.</summary>
        TlsFailure,

        /// <summary>The credentials were presented and refused.</summary>
        Authentication,

        /// <summary>A private key was found but could not be read — wrong passphrase, or an unsupported format.</summary>
        PrivateKey,

        /// <summary>Authenticated, but the account is not allowed to do this.</summary>
        Authorization,

        /// <summary>Something is listening, but it does not speak this protocol.</summary>
        ProtocolMismatch,

        /// <summary>The server is up and refusing new sessions: licence limit, max connections, shutting down.</summary>
        ServerBusy,

        /// <summary>The proxy or jump host between here and the target is what failed.</summary>
        Proxy,

        /// <summary>The attempt was abandoned deliberately — the tab was closed, the user cancelled.</summary>
        Cancelled,
    }

    /// <summary>
    /// A classified connection failure: what kind it is, whether trying again could plausibly succeed
    /// without the user changing anything, and the original message so nothing is hidden.
    /// </summary>
    public sealed class ConnectionFailure
    {
        public ConnectionFailure(EConnectionFailure kind, string rawMessage)
        {
            Kind = kind;
            RawMessage = rawMessage ?? "";
        }

        public EConnectionFailure Kind { get; }

        /// <summary>The message the underlying library produced, kept verbatim for the log and the details line.</summary>
        public string RawMessage { get; }

        /// <summary>
        /// Whether an unchanged retry has a real chance. A refused password does not become right by being
        /// sent again, and a name that does not resolve does not resolve on the second look either; a
        /// timeout or a dropped connection often does.
        /// </summary>
        public bool IsRetryable => Kind switch
        {
            EConnectionFailure.Timeout => true,
            EConnectionFailure.ConnectionDropped => true,
            EConnectionFailure.NetworkUnreachable => true,
            EConnectionFailure.ServerBusy => true,
            EConnectionFailure.Refused => true,
            EConnectionFailure.Proxy => true,
            EConnectionFailure.Unknown => true,
            _ => false,
        };

        /// <summary>The resource key of the sentence explaining what to do about <see cref="Kind"/>.</summary>
        public string HintKey => "conn_fail_" + Kind switch
        {
            EConnectionFailure.NameResolution => "dns",
            EConnectionFailure.Refused => "refused",
            EConnectionFailure.Timeout => "timeout",
            EConnectionFailure.NetworkUnreachable => "unreachable",
            EConnectionFailure.ConnectionDropped => "dropped",
            EConnectionFailure.HostIdentityRejected => "host_identity",
            EConnectionFailure.TlsFailure => "tls",
            EConnectionFailure.Authentication => "auth",
            EConnectionFailure.PrivateKey => "private_key",
            EConnectionFailure.Authorization => "authorization",
            EConnectionFailure.ProtocolMismatch => "protocol",
            EConnectionFailure.ServerBusy => "busy",
            EConnectionFailure.Proxy => "proxy",
            EConnectionFailure.Cancelled => "cancelled",
            _ => "unknown",
        };
    }
}
