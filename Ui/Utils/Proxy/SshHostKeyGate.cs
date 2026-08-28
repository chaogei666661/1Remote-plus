using System;
using Shawn.Utils;

namespace _1RM.Utils.Proxy
{
    /// <summary>
    /// Decides whether the host key an SSH jump host presented may be trusted.
    ///
    /// SSH.NET accepts every host key unless a <c>HostKeyReceived</c> handler says otherwise.
    /// <c>TransmitterSFtp</c> was given such a handler when <see cref="Service.HostTrustService"/> was
    /// written; <see cref="SshConnectionFactory"/> never was, so the one SSH connection this app makes
    /// itself — the bastion — was the one it did not verify. That is the worse of the two:
    ///
    /// <list type="bullet">
    /// <item>The jump host's password, or the passphrase-unlocked private key, is offered to whatever
    /// answers on that address, before anything else happens.</item>
    /// <item>Every session routed through the bastion, and every standing port forward, runs inside that
    /// unauthenticated transport — so intercepting one SSH handshake yields the credentials of all of
    /// them.</item>
    /// <item>An auto-started forward dials on launch, with no user watching, and would have gone through
    /// silently.</item>
    /// </list>
    ///
    /// The decision lives here rather than inside the factory for two reasons. SSH.NET's
    /// <c>HostKeyEventArgs</c> cannot be constructed outside the library, so a handler written inline would
    /// be untestable; and this file then depends on nothing but the delegate, which is what lets the rules
    /// below be exercised without a window.
    /// </summary>
    public static class SshHostKeyGate
    {
        /// <summary>
        /// Answers the trust question for one key. <paramref name="detail"/> is extra context for the
        /// prompt — which proxy entry is dialling, and the host key algorithm.
        /// </summary>
        public delegate bool VerifyDelegate(string host, int port, byte[] hostKey, string detail);

        /// <summary>
        /// Wired at start-up to <see cref="Service.HostTrustService"/>, and replaced in tests.
        ///
        /// The default refuses. A gate that opened when nobody had connected it would be worse than no
        /// gate, because it would look like one; failing closed turns a wiring mistake into a login that
        /// does not work rather than a check that silently does not happen.
        /// </summary>
        public static VerifyDelegate Verify { get; set; } = RefuseUntilWired;

        private static bool RefuseUntilWired(string host, int port, byte[] hostKey, string detail)
        {
            SimpleLogHelper.Error($"SshHostKeyGate: no verifier is wired, refusing {host}:{port}");
            return false;
        }

        /// <summary>
        /// Whether the connection to <paramref name="host"/> may proceed.
        /// </summary>
        /// <param name="hostKey">The key bytes SSH.NET reported, as sent by the server.</param>
        public static bool IsTrusted(string host, int port, byte[]? hostKey, string detail = "")
        {
            // A key we cannot see is a key we cannot pin, and the next connection would have nothing to
            // compare against — so this is a refusal and not a "remember an empty fingerprint".
            if (hostKey == null || hostKey.Length == 0)
            {
                SimpleLogHelper.Warning($"SshHostKeyGate: {host}:{port} presented no host key, refusing");
                return false;
            }

            if (string.IsNullOrWhiteSpace(host))
            {
                SimpleLogHelper.Warning("SshHostKeyGate: refusing a host key for an empty address");
                return false;
            }

            try
            {
                return Verify(host, port, hostKey, detail ?? "");
            }
            catch (Exception e)
            {
                // The verifier reads a file and puts a dialog on the screen, either of which can throw.
                // This runs on SSH.NET's receive thread, where an escaping exception would not be a
                // failed login but a dead process.
                SimpleLogHelper.Error($"SshHostKeyGate: the verifier failed for {host}:{port}, refusing. {e.Message}");
                return false;
            }
        }
    }
}
