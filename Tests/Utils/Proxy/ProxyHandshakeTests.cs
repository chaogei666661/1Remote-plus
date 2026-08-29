using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using _1RM.Utils.Proxy;

namespace Tests.Utils.Proxy
{
    /// <summary>
    /// ProxyHandshake builds the SOCKS5/SOCKS4/SOCKS4a/HTTP-CONNECT byte streams that every proxied session
    /// depends on, and its own summary says it is meant to be tested against a MemoryStream — but it had no
    /// tests. A wrong length byte or address type here silently sends a session somewhere, so these pin the
    /// exact bytes written and the reply handling. The scripted stream hands back the server's canned reply
    /// on Read and captures what the client wrote.
    /// </summary>
    [TestClass]
    public class ProxyHandshakeTests
    {
        /// <summary>A stream whose reads come from a fixed script and whose writes are captured.</summary>
        private sealed class ScriptedStream : Stream
        {
            private readonly byte[] _serverReplies;
            private int _readPos;
            public readonly MemoryStream Written = new MemoryStream();

            public ScriptedStream(params byte[][] replies)
            {
                _serverReplies = replies.SelectMany(x => x).ToArray();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_readPos >= _serverReplies.Length) return 0;
                int n = Math.Min(count, _serverReplies.Length - _readPos);
                Array.Copy(_serverReplies, _readPos, buffer, offset, n);
                _readPos += n;
                return n;
            }

            public override void Write(byte[] buffer, int offset, int count) => Written.Write(buffer, offset, count);
            public byte[] WrittenBytes => Written.ToArray();

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }

        [TestMethod]
        public void Socks5NoAuthSendsGreetingAndConnectRequestForAHostname()
        {
            var s = new ScriptedStream(
                new byte[] { 0x05, 0x00 },             // method selection: no auth
                new byte[] { 0x05, 0x00, 0x00, 0x01 }, // connect reply: success, bound IPv4 follows
                new byte[] { 0, 0, 0, 0, 0, 0 });      // bound addr (4) + port (2)

            ProxyHandshake.Perform(s, EProxyType.Socks5, "example.com", 443, "", "");

            var host = Encoding.UTF8.GetBytes("example.com");
            var expected = new byte[] { 0x05, 0x01, 0x00 }                                   // greeting
                .Concat(new byte[] { 0x05, 0x01, 0x00, 0x03, (byte)host.Length })            // req header + ATYP domain
                .Concat(host)
                .Concat(new byte[] { 0x01, 0xBB })                                           // port 443
                .ToArray();
            CollectionAssert.AreEqual(expected, s.WrittenBytes);
        }

        [TestMethod]
        public void Socks5UsesAtyp1ForIpv4AndAtyp4ForIpv6()
        {
            var v4 = new ScriptedStream(
                new byte[] { 0x05, 0x00 },
                new byte[] { 0x05, 0x00, 0x00, 0x01 }, new byte[] { 0, 0, 0, 0, 0, 0 });
            ProxyHandshake.Perform(v4, EProxyType.Socks5, "10.0.0.1", 22, "", "");
            // greeting(3) then 05 01 00 01 <4 bytes> <port>
            var v4req = v4.WrittenBytes.Skip(3).ToArray();
            CollectionAssert.AreEqual(new byte[] { 0x05, 0x01, 0x00, 0x01, 10, 0, 0, 1, 0x00, 0x16 }, v4req);

            var v6 = new ScriptedStream(
                new byte[] { 0x05, 0x00 },
                new byte[] { 0x05, 0x00, 0x00, 0x01 }, new byte[] { 0, 0, 0, 0, 0, 0 });
            ProxyHandshake.Perform(v6, EProxyType.Socks5, "::1", 22, "", "");
            var v6req = v6.WrittenBytes.Skip(3).ToArray();
            Assert.AreEqual(0x04, v6req[3]); // ATYP IPv6
            Assert.AreEqual(16 + 4 + 2, v6req.Length); // header(4) + 16 addr + 2 port
        }

        [TestMethod]
        public void Socks5SendsUsernamePasswordSubNegotiationWhenTheProxyAsksForIt()
        {
            var s = new ScriptedStream(
                new byte[] { 0x05, 0x02 },             // selected user/pass auth
                new byte[] { 0x01, 0x00 },             // auth success
                new byte[] { 0x05, 0x00, 0x00, 0x01 }, new byte[] { 0, 0, 0, 0, 0, 0 });

            ProxyHandshake.Perform(s, EProxyType.Socks5, "10.0.0.1", 22, "u", "pw");

            var written = s.WrittenBytes;
            // greeting advertises auth: 05 02 00 02
            CollectionAssert.AreEqual(new byte[] { 0x05, 0x02, 0x00, 0x02 }, written.Take(4).ToArray());
            // then the RFC 1929 sub-negotiation: 01 <ulen> u <plen> pw
            var auth = written.Skip(4).Take(1 + 1 + 1 + 1 + 2).ToArray();
            CollectionAssert.AreEqual(
                new byte[] { 0x01, 0x01, (byte)'u', 0x02, (byte)'p', (byte)'w' }, auth);
        }

        [TestMethod]
        public void Socks5RaisesADescriptiveErrorWhenTheProxyRefuses()
        {
            var s = new ScriptedStream(
                new byte[] { 0x05, 0x00 },
                new byte[] { 0x05, 0x05, 0x00, 0x01 }); // 0x05 = connection refused
            var ex = Assert.ThrowsException<IOException>(
                () => ProxyHandshake.Perform(s, EProxyType.Socks5, "10.0.0.1", 22, "", ""));
            StringAssert.Contains(ex.Message, "connection refused");
        }

        [TestMethod]
        public void Socks5RejectsAHostnameLongerThan255Bytes()
        {
            var s = new ScriptedStream(new byte[] { 0x05, 0x00 });
            Assert.ThrowsException<IOException>(
                () => ProxyHandshake.Perform(s, EProxyType.Socks5, new string('a', 256), 80, "", ""));
        }

        [TestMethod]
        public void Socks4WithAnIpv4TargetSendsTheRequestAndAcceptsGranted()
        {
            var s = new ScriptedStream(new byte[] { 0x00, 0x5A, 0, 0, 0, 0, 0, 0 });
            ProxyHandshake.Perform(s, EProxyType.Socks4, "10.0.0.1", 8080, "me", "");

            var expected = new byte[] { 0x04, 0x01, 0x1F, 0x90, 10, 0, 0, 1 } // VN,CD,port 8080,ip
                .Concat(Encoding.UTF8.GetBytes("me"))
                .Concat(new byte[] { 0x00 })
                .ToArray();
            CollectionAssert.AreEqual(expected, s.WrittenBytes);
        }

        [TestMethod]
        public void Socks4aAppendsTheHostnameAfterAZeroDotAddress()
        {
            var s = new ScriptedStream(new byte[] { 0x00, 0x5A, 0, 0, 0, 0, 0, 0 });
            ProxyHandshake.Perform(s, EProxyType.Socks4A, "example.com", 22, "", "");

            var expected = new byte[] { 0x04, 0x01, 0x00, 0x16, 0x00, 0x00, 0x00, 0x01, 0x00 } // ...userid(empty) 00
                .Concat(Encoding.UTF8.GetBytes("example.com"))
                .Concat(new byte[] { 0x00 })
                .ToArray();
            CollectionAssert.AreEqual(expected, s.WrittenBytes);
        }

        [TestMethod]
        public void Socks4RaisesADescriptiveErrorWhenRejected()
        {
            var s = new ScriptedStream(new byte[] { 0x00, 0x5B, 0, 0, 0, 0, 0, 0 });
            var ex = Assert.ThrowsException<IOException>(
                () => ProxyHandshake.Perform(s, EProxyType.Socks4, "10.0.0.1", 22, "", ""));
            StringAssert.Contains(ex.Message, "rejected");
        }

        [TestMethod]
        public void HttpConnectWritesTheRequestAndAcceptsA200()
        {
            var s = new ScriptedStream(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n"));
            ProxyHandshake.Perform(s, EProxyType.Http, "example.com", 443, "", "");

            var text = Encoding.ASCII.GetString(s.WrittenBytes);
            StringAssert.StartsWith(text, "CONNECT example.com:443 HTTP/1.1\r\n");
            StringAssert.Contains(text, "Host: example.com:443\r\n");
            StringAssert.Contains(text, "\r\n\r\n");
        }

        [TestMethod]
        public void HttpConnectBracketsAnIpv6AuthorityAndSendsBasicAuth()
        {
            var s = new ScriptedStream(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n\r\n"));
            ProxyHandshake.Perform(s, EProxyType.Http, "::1", 443, "user", "pass");

            var text = Encoding.ASCII.GetString(s.WrittenBytes);
            StringAssert.StartsWith(text, "CONNECT [::1]:443 HTTP/1.1\r\n");
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass"));
            StringAssert.Contains(text, "Proxy-Authorization: Basic " + token + "\r\n");
        }

        [TestMethod]
        public void HttpConnectTreats407AsAnAuthFailure()
        {
            var s = new ScriptedStream(Encoding.ASCII.GetBytes("HTTP/1.1 407 Proxy Authentication Required\r\n\r\n"));
            var ex = Assert.ThrowsException<IOException>(
                () => ProxyHandshake.Perform(s, EProxyType.Http, "example.com", 443, "", ""));
            StringAssert.Contains(ex.Message, "407");
        }

        [TestMethod]
        public void PerformValidatesTheTarget()
        {
            var s = new ScriptedStream();
            Assert.ThrowsException<ArgumentException>(
                () => ProxyHandshake.Perform(s, EProxyType.Socks5, "  ", 443, "", ""));
            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => ProxyHandshake.Perform(s, EProxyType.Socks5, "example.com", 0, "", ""));
            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => ProxyHandshake.Perform(s, EProxyType.Socks5, "example.com", 70000, "", ""));
        }
    }
}
