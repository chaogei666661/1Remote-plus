using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using _1RM.Utils;

namespace Tests.Utils
{
    /// <summary>
    /// The address-column sort used to compare IPv4 octets and ports as text and to reject any compressed
    /// IPv6 address. These cover the cases that were wrong: numeric octets, numeric ports, compressed and
    /// bracketed IPv6, natural host ordering, and the family ranking.
    /// </summary>
    [TestClass]
    public class HostNaturalSortTests
    {
        private static void AssertBefore(string a, string b)
        {
            Assert.IsTrue(HostNaturalSort.Compare(a, b) < 0, $"'{a}' should sort before '{b}'");
            Assert.IsTrue(HostNaturalSort.Compare(b, a) > 0, $"'{b}' should sort after '{a}' (antisymmetry)");
        }

        [TestMethod]
        public void Ipv4IsOrderedByNumericValueNotText()
        {
            // The bug: "192.168.0.10" sorted before "192.168.0.2" because '1' < '2' as characters.
            AssertBefore("192.168.0.2", "192.168.0.10");
            AssertBefore("10.0.0.9", "10.0.0.10");
        }

        [TestMethod]
        public void PortsAreOrderedByNumericValueNotText()
        {
            AssertBefore("10.0.0.1:9", "10.0.0.1:10");
            // No port sorts before any explicit port on the same address.
            AssertBefore("10.0.0.1", "10.0.0.1:1");
        }

        [TestMethod]
        public void CompressedIpv6IsRecognisedAndOrdered()
        {
            // "::1" and "fe80::1" were both rejected before and fell through to a raw string compare.
            AssertBefore("::1", "fe80::1");
            AssertBefore("2001:db8::1", "2001:db8::2");
        }

        [TestMethod]
        public void BracketedIpv6WithPortIsRecognised()
        {
            // The address must not be cut at its first colon; the whole thing is one IPv6 host plus a port.
            Assert.IsTrue(HostNaturalSort.Compare("[::1]:22", "[::1]:8") > 0, "port 22 sorts after port 8");
            AssertBefore("10.0.0.1", "[::1]:22"); // IPv4 still sorts before IPv6
        }

        [TestMethod]
        public void Ipv4SortsBeforeIpv6SortsBeforeHostnames()
        {
            AssertBefore("10.0.0.1", "fe80::1");
            AssertBefore("fe80::1", "server1");
            AssertBefore("10.0.0.1", "server1");
        }

        [TestMethod]
        public void HostnamesSortNaturally()
        {
            AssertBefore("pc2", "pc10");
            AssertBefore("node9", "node10");
            AssertBefore("web", "web2");
        }

        [TestMethod]
        public void LargeNumericRunsDoNotOverflow()
        {
            AssertBefore("host99999999999999999999", "host100000000000000000000");
        }

        [TestMethod]
        public void NullsAndBlanksAreHandled()
        {
            Assert.AreEqual(0, HostNaturalSort.Compare(null, null));
            Assert.AreEqual(0, HostNaturalSort.Compare(null, ""));
            Assert.IsTrue(HostNaturalSort.Compare(null, "server") < 0);
            Assert.IsTrue(HostNaturalSort.Compare("server", null) > 0);
        }

        [TestMethod]
        public void SortingAMixedListProducesTheHumanExpectedOrder()
        {
            var list = new List<string>
            {
                "server10", "10.0.0.2", "server2", "fe80::1",
                "10.0.0.10", "::1", "10.0.0.2:22", "10.0.0.2:8",
            };
            list.Sort(HostNaturalSort.Compare);

            CollectionAssert.AreEqual(new List<string>
            {
                "10.0.0.2", "10.0.0.2:8", "10.0.0.2:22", "10.0.0.10", // IPv4 by address then port
                "::1", "fe80::1",                                      // then IPv6 by address
                "server2", "server10",                                // then hostnames, naturally
            }, list);
        }
    }
}
