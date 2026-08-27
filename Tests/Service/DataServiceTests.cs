using _1RM.Model.Protocol;
using _1RM.Model.Protocol.Base;
using _1RM.Service;
using _1RM.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Tests.Service
{
    /// <summary>
    /// The one place a stored password becomes a usable one. The cipher is obfuscation rather than
    /// per-user encryption — see the security notes in the readme — so what these tests pin down is the
    /// behaviour every protocol depends on: a round trip returns exactly what went in, and a value that was
    /// already stored is not enciphered a second time.
    /// </summary>
    [TestClass]
    public class DataServiceTests
    {
        [TestInitialize]
        public void Setup() => TestInit.Init();

        [TestMethod]
        public void ASecretSurvivesARoundTrip()
        {
            const string secret = "hunter2 with a space, a ünicode character and a \" quote";

            var stored = UnSafeStringEncipher.SimpleEncrypt(secret);

            Assert.AreNotEqual(secret, stored, "the stored form must not be the plain text");
            Assert.AreEqual(secret, UnSafeStringEncipher.SimpleDecrypt(stored));
        }

        [TestMethod]
        public void EncryptingTwiceIsTheSameAsEncryptingOnce()
        {
            // Servers get saved repeatedly, and every save runs EncryptToDatabaseLevel over the same object.
            var once = UnSafeStringEncipher.EncryptOnce("hunter2");
            var twice = UnSafeStringEncipher.EncryptOnce(once);

            Assert.AreEqual("hunter2", UnSafeStringEncipher.DecryptOrReturnOriginalString(twice));
        }

        [TestMethod]
        public void PlainTextThatWasNeverEncipheredComesBackUnchanged()
        {
            // An imported or hand-edited database holds passwords in the clear, and reading one must not
            // turn into an error or into garbage.
            Assert.AreEqual("hunter2", UnSafeStringEncipher.DecryptOrReturnOriginalString("hunter2"));
            Assert.IsNull(UnSafeStringEncipher.SimpleDecrypt("hunter2"));
        }

        [TestMethod]
        public void AServerRoundTripsThroughTheDatabaseLevel()
        {
            var ssh = new SSH { Address = "10.0.0.1", UserName = "root", Password = "hunter2" };
            ssh.AlternateCredentials.Add(new Credential { Name = "backup", UserName = "ops", Password = "correct horse" });

            ssh.EncryptToDatabaseLevel();

            Assert.AreNotEqual("hunter2", ssh.Password);
            Assert.AreNotEqual("correct horse", ssh.AlternateCredentials[0].Password);
            var asStored = JsonConvert.SerializeObject(ssh);
            Assert.IsFalse(asStored.Contains("hunter2"), "the database row must not hold the password in the clear");
            Assert.IsFalse(asStored.Contains("correct horse"));

            ssh.DecryptToConnectLevel();

            Assert.AreEqual("hunter2", ssh.Password);
            Assert.AreEqual("correct horse", ssh.AlternateCredentials[0].Password);
        }

        [TestMethod]
        public void AnRdpGatewayPasswordIsEncipheredToo()
        {
            var rdp = new RDP { Address = "10.0.0.1", UserName = "admin", GatewayPassword = "gateway secret" };

            rdp.EncryptToDatabaseLevel();
            Assert.AreNotEqual("gateway secret", rdp.GatewayPassword);

            rdp.DecryptToConnectLevel();
            Assert.AreEqual("gateway secret", rdp.GatewayPassword);
        }

        [TestMethod]
        public void OnlySecretAppArgumentsAreEnciphered()
        {
            var app = new LocalApp { ExePath = @"C:\tools\thing.exe" };
            app.ArgumentList.Add(new AppArgument { Name = "token", Key = "--token", Type = AppArgumentType.Secret, Value = "hunter2" });
            app.ArgumentList.Add(new AppArgument { Name = "host", Key = "--host", Value = "10.0.0.1" });

            app.EncryptToDatabaseLevel();

            Assert.AreNotEqual("hunter2", app.ArgumentList[0].Value);
            Assert.AreEqual("10.0.0.1", app.ArgumentList[1].Value, "a plain argument is not a secret");

            app.DecryptToConnectLevel();

            Assert.AreEqual("hunter2", app.ArgumentList[0].Value);
        }

        [TestMethod]
        public void AnEmptyPasswordStaysEmpty()
        {
            var ssh = new SSH { Address = "10.0.0.1", UserName = "root", Password = "" };

            ssh.EncryptToDatabaseLevel();
            ssh.DecryptToConnectLevel();

            Assert.AreEqual("", ssh.Password, "a blank password has to come back blank, not as cipher text");
        }
    }
}
