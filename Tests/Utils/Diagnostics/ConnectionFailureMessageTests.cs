using System;
using _1RM.Utils.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.Diagnostics
{
    [TestClass]
    public class ConnectionFailureMessageTests
    {
        /// <summary>Echoes the key back, so a case can assert on which strings were asked for.</summary>
        private static string Echo(string key) => key;

        [TestMethod]
        public void TheHintTheEndpointAndTheRawTextAreAllPresent()
        {
            var text = ConnectionFailureMessage.Build(
                new ConnectionFailure(EConnectionFailure.Refused, "No connection could be made"),
                "srv01:22", Echo);

            StringAssert.Contains(text, "conn_fail_refused");
            StringAssert.Contains(text, "srv01:22");
            StringAssert.Contains(text, "No connection could be made");
        }

        [TestMethod]
        public void AnEmptyRawMessageDoesNotLeaveADanglingLabel()
        {
            var text = ConnectionFailureMessage.Build(
                new ConnectionFailure(EConnectionFailure.ConnectionDropped, ""), "srv01:5900", Echo);

            Assert.IsFalse(text.Contains("conn_fail_details"));
            StringAssert.Contains(text, "conn_fail_dropped");
        }

        [TestMethod]
        public void AnEmptyEndpointIsOmitted()
        {
            var text = ConnectionFailureMessage.Build(
                new ConnectionFailure(EConnectionFailure.Timeout, "timed out"), "", Echo);

            Assert.IsFalse(text.Contains("conn_fail_endpoint"));
        }

        [TestMethod]
        public void ARawMessageThatOnlyRepeatsTheHintIsNotShownTwice()
        {
            var text = ConnectionFailureMessage.Build(
                new ConnectionFailure(EConnectionFailure.Timeout, "conn_fail_timeout"), "", Echo);

            Assert.IsFalse(text.Contains("conn_fail_details"));
        }

        [TestMethod]
        public void ItRejectsMissingArguments()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                ConnectionFailureMessage.Build(null!, "", Echo));
            Assert.ThrowsException<ArgumentNullException>(() =>
                ConnectionFailureMessage.Build(new ConnectionFailure(EConnectionFailure.Unknown, ""), "", null!));
        }
    }
}
