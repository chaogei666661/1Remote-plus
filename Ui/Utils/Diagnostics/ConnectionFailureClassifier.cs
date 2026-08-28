using System;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;

namespace _1RM.Utils.Diagnostics
{
    /// <summary>
    /// Turns whatever a transport threw into an <see cref="EConnectionFailure"/>.
    ///
    /// RDP already had <see cref="Rdp.RdpDisconnectClassifier"/>, because the ActiveX control reports a
    /// numeric code. Everything else — SSH.NET, FluentFTP, VncSharpCore, the proxy tunnels — reports an
    /// exception, and the three hosts that catch one each printed <c>e.Message</c> into a panel. That message
    /// is written for a developer reading a stack trace, so a user staring at "No such host is known" is not
    /// told that the name is the problem, and nothing tells the UI whether a retry button is worth offering.
    ///
    /// Classification is deliberately by socket error code first and by exception type second, and only then
    /// by the text of the message. Matching on text is fragile across library versions and locales, so it is
    /// the last resort rather than the mechanism — but SSH.NET and VncSharpCore both raise plain
    /// <c>Exception</c> for cases the user very much needs told apart, so it cannot be avoided entirely.
    /// </summary>
    public static class ConnectionFailureClassifier
    {
        public static ConnectionFailure Classify(Exception? exception)
        {
            if (exception == null)
                return new ConnectionFailure(EConnectionFailure.Unknown, "");

            var raw = DeepestMessage(exception);
            return new ConnectionFailure(ClassifyKind(exception), raw);
        }

        /// <summary>
        /// For call sites that only have a message — VncSharpCore's connection-lost event, a runner's
        /// stderr, a tunnel that reported a string.
        /// </summary>
        public static ConnectionFailure ClassifyMessage(string? message)
        {
            var raw = message ?? "";
            return new ConnectionFailure(ClassifyText(raw), raw);
        }

        private static EConnectionFailure ClassifyKind(Exception exception)
        {
            // Walk in, not out: an AggregateException or a TargetInvocationException says nothing, and
            // SSH.NET habitually wraps the SocketException that actually knows what went wrong.
            for (var e = exception; e != null; e = e.InnerException)
            {
                var kind = ClassifyOne(e);
                if (kind != EConnectionFailure.Unknown)
                    return kind;
            }

            if (exception is AggregateException aggregate)
            {
                foreach (var inner in aggregate.Flatten().InnerExceptions)
                {
                    var kind = ClassifyKind(inner);
                    if (kind != EConnectionFailure.Unknown)
                        return kind;
                }
            }

            return ClassifyText(DeepestMessage(exception));
        }

        private static EConnectionFailure ClassifyOne(Exception e)
        {
            switch (e)
            {
                case SocketException socket:
                    return FromSocketError(socket.SocketErrorCode);
                case AuthenticationException:
                    return EConnectionFailure.TlsFailure;
                case OperationCanceledException:
                case ThreadInterruptedException:
                    return EConnectionFailure.Cancelled;
                case TimeoutException:
                    return EConnectionFailure.Timeout;
                case UnauthorizedAccessException:
                    return EConnectionFailure.Authorization;
            }

            // SSH.NET and FluentFTP are referenced by Ui, but naming their exception types here would tie a
            // leaf helper to two packages and to their current versions. The type name carries the same
            // information and survives a major-version bump that moves a namespace.
            switch (e.GetType().Name)
            {
                case "SshAuthenticationException":
                case "FtpAuthenticationException":
                    return EConnectionFailure.Authentication;
                case "SshPassPhraseNullOrEmptyException":
                    return EConnectionFailure.PrivateKey;
                case "SshOperationTimeoutException":
                    return EConnectionFailure.Timeout;
                case "SshConnectionException":
                case "FtpMissingSocketException":
                    return EConnectionFailure.ConnectionDropped;
                case "ProxyException":
                    return EConnectionFailure.Proxy;
                case "SshException":
                case "FtpException":
                case "FtpCommandException":
                    // Too broad to mean anything on its own; the message below is more specific.
                    return EConnectionFailure.Unknown;
            }

            return EConnectionFailure.Unknown;
        }

        private static EConnectionFailure FromSocketError(SocketError error)
        {
            switch (error)
            {
                case SocketError.HostNotFound:
                case SocketError.NoData:
                case SocketError.TryAgain:
                    return EConnectionFailure.NameResolution;

                case SocketError.ConnectionRefused:
                    return EConnectionFailure.Refused;

                case SocketError.TimedOut:
                    return EConnectionFailure.Timeout;

                case SocketError.NetworkDown:
                case SocketError.NetworkUnreachable:
                case SocketError.HostUnreachable:
                case SocketError.HostDown:
                case SocketError.AddressNotAvailable:
                    return EConnectionFailure.NetworkUnreachable;

                case SocketError.ConnectionReset:
                case SocketError.ConnectionAborted:
                case SocketError.Shutdown:
                case SocketError.Disconnecting:
                    return EConnectionFailure.ConnectionDropped;

                case SocketError.OperationAborted:
                case SocketError.Interrupted:
                    return EConnectionFailure.Cancelled;

                case SocketError.AccessDenied:
                    return EConnectionFailure.Authorization;

                default:
                    return EConnectionFailure.Unknown;
            }
        }

        /// <summary>
        /// Last resort. Only phrases that are stable across versions of the library that produces them and
        /// that map to exactly one category are listed; anything ambiguous is left as Unknown, which the UI
        /// renders as "the server said this" plus the raw text.
        /// </summary>
        private static EConnectionFailure ClassifyText(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return EConnectionFailure.Unknown;

            var m = message.ToLowerInvariant();

            if (Has(m, "no such host", "name or service not known", "host not found", "could not be resolved", "getaddrinfo"))
                return EConnectionFailure.NameResolution;

            if (Has(m, "actively refused", "connection refused"))
                return EConnectionFailure.Refused;

            if (Has(m, "timed out", "timeout", "did not properly respond after a period of time"))
                return EConnectionFailure.Timeout;

            if (Has(m, "network is unreachable", "no route to host", "host is unreachable", "network is down"))
                return EConnectionFailure.NetworkUnreachable;

            // Before the generic auth phrases: an SSH host key mismatch also says "denied" in some servers'
            // wording, and the identity problem is the one the user has to look at first.
            if (Has(m, "host key", "hostkey", "fingerprint", "known_hosts", "server's host key"))
                return EConnectionFailure.HostIdentityRejected;

            if (Has(m, "certificate", "ssl", "tls", "secure channel", "handshake failed"))
                return EConnectionFailure.TlsFailure;

            if (Has(m, "passphrase", "invalid private key", "unsupported key", "key file", "openssh key", "pem"))
                return EConnectionFailure.PrivateKey;

            if (Has(m, "permission denied", "authentication failed", "auth fail", "access denied",
                       "login failed", "invalid password", "wrong password", "bad password",
                       "incorrect password", "530 ", "not authorised", "not authorized"))
                return EConnectionFailure.Authentication;

            if (Has(m, "too many connections", "max clients", "server is busy", "resource temporarily unavailable",
                       "no more connections", "connection limit"))
                return EConnectionFailure.ServerBusy;

            if (Has(m, "proxy", "socks", "jump host"))
                return EConnectionFailure.Proxy;

            if (Has(m, "protocol version", "not a valid", "unexpected response", "invalid response",
                       "unsupported protocol", "rfb"))
                return EConnectionFailure.ProtocolMismatch;

            if (Has(m, "connection lost", "connection was reset", "connection reset", "connection closed",
                       "closed by the remote", "an established connection was aborted", "disconnected"))
                return EConnectionFailure.ConnectionDropped;

            if (Has(m, "cancelled", "canceled", "aborted by the user"))
                return EConnectionFailure.Cancelled;

            return EConnectionFailure.Unknown;
        }

        private static bool Has(string haystack, params string[] needles)
        {
            foreach (var needle in needles)
                if (haystack.Contains(needle))
                    return true;
            return false;
        }

        /// <summary>
        /// The innermost message. The outer one is usually "One or more errors occurred" or the library's
        /// own restatement, and the inner one is what the network actually said.
        /// </summary>
        private static string DeepestMessage(Exception exception)
        {
            var current = exception;
            while (current.InnerException != null)
                current = current.InnerException;
            if (current is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
                current = aggregate.InnerExceptions[0];
            return current.Message ?? "";
        }
    }
}
