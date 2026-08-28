using System;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using _1RM.Utils.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.Diagnostics
{
    /// <summary>
    /// The SFTP, FTP and VNC hosts used to print the raw exception message into their error panel. These
    /// cases pin the mapping from what the transports actually throw to the category the panel uses to
    /// decide what to tell the user and whether a retry is worth offering.
    /// </summary>
    [TestClass]
    public class ConnectionFailureClassifierTests
    {
        /// <summary>SSH.NET raises its own exception types; naming them here would pull the package into a
        /// test of a leaf helper, and the classifier matches on the type name for the same reason.</summary>
        private class SshAuthenticationException : Exception
        {
            public SshAuthenticationException(string message) : base(message) { }
        }

        private class SshConnectionException : Exception
        {
            public SshConnectionException(string message) : base(message) { }
        }

        private class SshOperationTimeoutException : Exception
        {
            public SshOperationTimeoutException(string message) : base(message) { }
        }

        private class SshPassPhraseNullOrEmptyException : Exception
        {
            public SshPassPhraseNullOrEmptyException(string message) : base(message) { }
        }

        private class FtpAuthenticationException : Exception
        {
            public FtpAuthenticationException(string message) : base(message) { }
        }

        [TestMethod]
        public void ANameThatDoesNotResolveIsNotATimeout()
        {
            var f = ConnectionFailureClassifier.Classify(new SocketException((int)SocketError.HostNotFound));
            Assert.AreEqual(EConnectionFailure.NameResolution, f.Kind);
            Assert.IsFalse(f.IsRetryable, "resolving the same name again will not help");
        }

        [TestMethod]
        public void ARefusedPortIsToldApartFromAFilteredOne()
        {
            Assert.AreEqual(EConnectionFailure.Refused,
                ConnectionFailureClassifier.Classify(new SocketException((int)SocketError.ConnectionRefused)).Kind);
            Assert.AreEqual(EConnectionFailure.Timeout,
                ConnectionFailureClassifier.Classify(new SocketException((int)SocketError.TimedOut)).Kind);
        }

        [TestMethod]
        public void NoRouteReadsAsUnreachableRatherThanAsTheHostBeingDown()
        {
            Assert.AreEqual(EConnectionFailure.NetworkUnreachable,
                ConnectionFailureClassifier.Classify(new SocketException((int)SocketError.NetworkUnreachable)).Kind);
            Assert.AreEqual(EConnectionFailure.NetworkUnreachable,
                ConnectionFailureClassifier.Classify(new SocketException((int)SocketError.HostUnreachable)).Kind);
        }

        [TestMethod]
        public void AResetIsRetryable()
        {
            var f = ConnectionFailureClassifier.Classify(new SocketException((int)SocketError.ConnectionReset));
            Assert.AreEqual(EConnectionFailure.ConnectionDropped, f.Kind);
            Assert.IsTrue(f.IsRetryable);
        }

        [TestMethod]
        public void TheSocketErrorInsideAWrapperIsFound()
        {
            // SSH.NET wraps the socket error; the outer message ("An error occurred while connecting") is
            // what the panel used to show.
            var wrapped = new SshConnectionException("An error occurred while connecting")
            {
            };
            var withInner = new InvalidOperationException("connect failed",
                new SshConnectionException("An error occurred while connecting"));
            Assert.AreEqual(EConnectionFailure.ConnectionDropped, ConnectionFailureClassifier.Classify(wrapped).Kind);
            Assert.AreEqual(EConnectionFailure.ConnectionDropped, ConnectionFailureClassifier.Classify(withInner).Kind);

            var socketInside = new Exception("outer", new Exception("middle", new SocketException((int)SocketError.ConnectionRefused)));
            Assert.AreEqual(EConnectionFailure.Refused, ConnectionFailureClassifier.Classify(socketInside).Kind);
        }

        [TestMethod]
        public void AnAggregateIsUnwrapped()
        {
            var aggregate = new AggregateException(new SocketException((int)SocketError.HostNotFound));
            Assert.AreEqual(EConnectionFailure.NameResolution, ConnectionFailureClassifier.Classify(aggregate).Kind);
        }

        [TestMethod]
        public void ARefusedPasswordIsNeverRetryable()
        {
            foreach (var e in new Exception[]
                     {
                         new SshAuthenticationException("Permission denied (password)."),
                         new FtpAuthenticationException("530 Login incorrect"),
                     })
            {
                var f = ConnectionFailureClassifier.Classify(e);
                Assert.AreEqual(EConnectionFailure.Authentication, f.Kind, e.GetType().Name);
                Assert.IsFalse(f.IsRetryable, e.GetType().Name);
            }
        }

        [TestMethod]
        public void APassphraseProblemIsNotReportedAsABadPassword()
        {
            Assert.AreEqual(EConnectionFailure.PrivateKey,
                ConnectionFailureClassifier.Classify(new SshPassPhraseNullOrEmptyException("Passphrase is required")).Kind);
            Assert.AreEqual(EConnectionFailure.PrivateKey,
                ConnectionFailureClassifier.ClassifyMessage("Invalid private key file.").Kind);
        }

        [TestMethod]
        public void AnSshTimeoutIsATimeout()
        {
            Assert.AreEqual(EConnectionFailure.Timeout,
                ConnectionFailureClassifier.Classify(new SshOperationTimeoutException("Session operation has timed out")).Kind);
            Assert.AreEqual(EConnectionFailure.Timeout,
                ConnectionFailureClassifier.Classify(new TimeoutException()).Kind);
        }

        [TestMethod]
        public void ATlsProblemIsItsOwnCategory()
        {
            Assert.AreEqual(EConnectionFailure.TlsFailure,
                ConnectionFailureClassifier.Classify(new AuthenticationException("The remote certificate is invalid")).Kind);
            Assert.AreEqual(EConnectionFailure.TlsFailure,
                ConnectionFailureClassifier.ClassifyMessage("The remote certificate is invalid according to the validation procedure.").Kind);
        }

        [TestMethod]
        public void AHostKeyProblemOutranksTheWordDenied()
        {
            // "Host key verification failed" also contains "failed"; the identity is the thing to look at.
            var f = ConnectionFailureClassifier.ClassifyMessage("Host key verification failed.");
            Assert.AreEqual(EConnectionFailure.HostIdentityRejected, f.Kind);
            Assert.IsFalse(f.IsRetryable);
        }

        [TestMethod]
        public void CancellationIsNotReportedAsAFailureToRetry()
        {
            Assert.AreEqual(EConnectionFailure.Cancelled,
                ConnectionFailureClassifier.Classify(new OperationCanceledException()).Kind);
            Assert.AreEqual(EConnectionFailure.Cancelled,
                ConnectionFailureClassifier.Classify(new TaskCanceledException()).Kind);
            Assert.AreEqual(EConnectionFailure.Cancelled,
                ConnectionFailureClassifier.Classify(new SocketException((int)SocketError.OperationAborted)).Kind);
            Assert.IsFalse(ConnectionFailureClassifier.Classify(new OperationCanceledException()).IsRetryable);
        }

        [TestMethod]
        public void ABusyServerIsRetryable()
        {
            var f = ConnectionFailureClassifier.ClassifyMessage("421 Too many connections from your IP");
            Assert.AreEqual(EConnectionFailure.ServerBusy, f.Kind);
            Assert.IsTrue(f.IsRetryable);
        }

        [TestMethod]
        public void SomethingOnTheWrongPortReadsAsAProtocolMismatch()
        {
            Assert.AreEqual(EConnectionFailure.ProtocolMismatch,
                ConnectionFailureClassifier.ClassifyMessage("Unsupported protocol version: RFB 999.999").Kind);
        }

        [TestMethod]
        public void NothingRecognisableStaysUnknownAndKeepsTheRawText()
        {
            var f = ConnectionFailureClassifier.Classify(new Exception("frobnicator misaligned"));
            Assert.AreEqual(EConnectionFailure.Unknown, f.Kind);
            Assert.AreEqual("frobnicator misaligned", f.RawMessage);
            Assert.IsTrue(f.IsRetryable, "an unclassified failure should still offer a retry");
        }

        [TestMethod]
        public void TheRawMessageComesFromTheInnermostException()
        {
            var e = new Exception("connect failed", new Exception("No such host is known."));
            Assert.AreEqual("No such host is known.", ConnectionFailureClassifier.Classify(e).RawMessage);
        }

        [TestMethod]
        public void ANullExceptionDoesNotThrow()
        {
            var f = ConnectionFailureClassifier.Classify(null);
            Assert.AreEqual(EConnectionFailure.Unknown, f.Kind);
            Assert.AreEqual("", f.RawMessage);
        }

        [TestMethod]
        public void EveryCategoryHasItsOwnHintKey()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (EConnectionFailure kind in Enum.GetValues(typeof(EConnectionFailure)))
            {
                var key = new ConnectionFailure(kind, "").HintKey;
                Assert.IsTrue(key.StartsWith("conn_fail_", StringComparison.Ordinal), key);
                Assert.IsTrue(seen.Add(key), $"{kind} shares a hint key with another category: {key}");
            }
        }

        [TestMethod]
        public void AnInterruptedThreadIsNotClassifiedAsANetworkProblem()
        {
            Assert.AreEqual(EConnectionFailure.Cancelled,
                ConnectionFailureClassifier.Classify(new ThreadInterruptedException()).Kind);
        }
    }
}
