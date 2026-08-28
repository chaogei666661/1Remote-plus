using _1RM.Service.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Service.Diagnostics
{
    /// <summary>
    /// The diagnostics bundle is written specifically in order to be sent to a stranger, so these are the
    /// cases that decide whether it is safe to press the button.
    /// </summary>
    [TestClass]
    public class DiagnosticsRedactorTests
    {
        [TestMethod]
        public void AJsonPasswordIsReplacedByItsLength()
        {
            var redacted = DiagnosticsRedactor.Redact("{\"UserName\": \"deploy\", \"Password\": \"hunter2\"}");
            Assert.IsFalse(redacted.Contains("hunter2"));
            StringAssert.Contains(redacted, "deploy");
            StringAssert.Contains(redacted, DiagnosticsRedactor.PLACEHOLDER + ":7");
        }

        [TestMethod]
        public void TheKeyNameIsMatchedAsASubstringAndCaseInsensitively()
        {
            foreach (var key in new[] { "Password", "password", "PrivateKeyPassphrase", "WebDavPassword", "ApiKey", "AuthToken" })
            {
                var redacted = DiagnosticsRedactor.Redact("{\"" + key + "\": \"topsecret\"}");
                Assert.IsFalse(redacted.Contains("topsecret"), key);
            }
        }

        [TestMethod]
        public void AnEmptySecretStaysEmptyRatherThanBecomingAPlaceholder()
        {
            // "was it even set?" is a real support question, and a placeholder would hide the answer.
            var redacted = DiagnosticsRedactor.Redact("{\"Password\": \"\"}");
            Assert.AreEqual("{\"Password\": \"\"}", redacted);
        }

        [TestMethod]
        public void AFieldThatIsNotASecretIsUntouched()
        {
            const string text = "{\"Address\": \"10.0.0.5\", \"Port\": 22, \"UserName\": \"deploy\"}";
            StringAssert.Contains(DiagnosticsRedactor.Redact(text), "10.0.0.5");
            StringAssert.Contains(DiagnosticsRedactor.Redact(text), "deploy");
        }

        [TestMethod]
        public void APasswordArgumentOnACommandLineIsRemoved()
        {
            // This is the realistic leak: a runner's argument template is free text the user wrote.
            var redacted = DiagnosticsRedactor.Redact("putty.exe -ssh user@host -pw hunter2 -P 22");
            Assert.IsFalse(redacted.Contains("hunter2"));
            StringAssert.Contains(redacted, "user@host");

            redacted = DiagnosticsRedactor.Redact("winscp.exe --password=hunter2");
            Assert.IsFalse(redacted.Contains("hunter2"));
        }

        [TestMethod]
        public void APemPrivateKeyBlockIsRemovedButItsPresenceIsVisible()
        {
            const string pem = "-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXktdjEAAAAA\nAAAABG5vbmU=\n-----END OPENSSH PRIVATE KEY-----";
            var redacted = DiagnosticsRedactor.Redact("before\n" + pem + "\nafter");

            Assert.IsFalse(redacted.Contains("b3BlbnNzaC1rZXktdjEAAAAA"));
            StringAssert.Contains(redacted, "BEGIN PRIVATE KEY");
            StringAssert.Contains(redacted, "before");
            StringAssert.Contains(redacted, "after");
        }

        [TestMethod]
        public void AnExternalSecretCommandIsRemoved()
        {
            // The command line is what fetches the password, so it is as sensitive as the password.
            var redacted = DiagnosticsRedactor.Redact("Password = cmd://bw get password prod-db");
            Assert.IsFalse(redacted.Contains("bw get password prod-db"));
        }

        [TestMethod]
        public void AnIniStylePasswordIsRemoved()
        {
            var redacted = DiagnosticsRedactor.Redact("host=10.0.0.5\npassword=hunter2\nuser=deploy");
            Assert.IsFalse(redacted.Contains("hunter2"));
            StringAssert.Contains(redacted, "10.0.0.5");
            StringAssert.Contains(redacted, "deploy");
        }

        [TestMethod]
        public void NullAndEmptyAreHandled()
        {
            Assert.AreEqual("", DiagnosticsRedactor.Redact(null));
            Assert.AreEqual("", DiagnosticsRedactor.Redact(""));
        }

        [TestMethod]
        public void IsSecretKeyAgreesWithWhatRedactActuallyStrips()
        {
            Assert.IsTrue(DiagnosticsRedactor.IsSecretKey("Password"));
            Assert.IsTrue(DiagnosticsRedactor.IsSecretKey("privateKey"));
            Assert.IsFalse(DiagnosticsRedactor.IsSecretKey("Address"));
            Assert.IsFalse(DiagnosticsRedactor.IsSecretKey(""));
            Assert.IsFalse(DiagnosticsRedactor.IsSecretKey(null));
        }

        [TestMethod]
        public void AHostIsShortenedToItsShapeRatherThanRemoved()
        {
            var a = DiagnosticsRedactor.RedactHost("prod-db-01.corp.example.com");
            var b = DiagnosticsRedactor.RedactHost("prod-web-02.corp.example.com");
            Assert.AreNotEqual("prod-db-01.corp.example.com", a);
            Assert.IsFalse(a.Contains("example"));
            Assert.AreEqual(a.Length, b.Length);
            Assert.AreEqual("", DiagnosticsRedactor.RedactHost(null));
            Assert.AreEqual("**", DiagnosticsRedactor.RedactHost("ab"));
        }

        [TestMethod]
        public void RedactionIsIdempotent()
        {
            // The bundle reads files that may already contain a placeholder from an earlier pass.
            var once = DiagnosticsRedactor.Redact("{\"Password\": \"hunter2\"}");
            Assert.AreEqual(once, DiagnosticsRedactor.Redact(once));
        }
    }
}
