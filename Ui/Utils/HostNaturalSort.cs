using System;
using System.Net;
using System.Net.Sockets;

namespace _1RM.Utils
{
    /// <summary>
    /// The ordering behind the server list's address ("SubTitle") column. Kept pure and windowless so it
    /// can be unit-tested — <see cref="SubTitleSortByNaturalIp"/> is a thin <c>IComparer</c> wrapper that
    /// pulls the two subtitles out of the view models and applies the sort direction.
    ///
    /// It fixes three things the old inline version got wrong:
    ///   * a compressed IPv6 address (<c>fe80::1</c>, <c>::1</c>) was rejected, because it required exactly
    ///     eight colon groups and split the address on its first colon as if it were a port separator;
    ///   * ports were compared as text, so <c>:10</c> sorted before <c>:9</c>;
    ///   * the ascending/descending flag was ignored for anything that parsed as an IP.
    ///
    /// Ordering: IPv4 addresses first (by numeric value), then IPv6 (by numeric value), then everything else
    /// in natural, case-insensitive order (so <c>pc2</c> precedes <c>pc10</c>). Ties break on the port
    /// number and finally the raw string, so the comparison is a total order.
    /// </summary>
    public static class HostNaturalSort
    {
        public static int Compare(string? x, string? y)
        {
            x ??= "";
            y ??= "";

            var (hostX, portX) = SplitHostAndPort(x);
            var (hostY, portY) = SplitHostAndPort(y);

            int rankX = Rank(hostX, out var ipX);
            int rankY = Rank(hostY, out var ipY);
            if (rankX != rankY)
                return rankX < rankY ? -1 : 1;

            int cmp = rankX switch
            {
                0 or 1 => CompareBytes(ipX!.GetAddressBytes(), ipY!.GetAddressBytes()),
                _ => CompareNatural(hostX, hostY),
            };
            if (cmp != 0)
                return cmp;

            cmp = ComparePort(portX, portY);
            if (cmp != 0)
                return cmp;

            // Same host and port: fall back to the raw string so equal keys keep a deterministic order.
            return string.Compare(x, y, StringComparison.Ordinal);
        }

        /// <summary>0 = IPv4, 1 = IPv6, 2 = not an IP literal.</summary>
        private static int Rank(string host, out IPAddress? ip)
        {
            // Require four dotted parts for IPv4 and a colon for IPv6, so a bare number ("8080") or a
            // hostname is not silently reinterpreted as an address by IPAddress.TryParse's lenient forms.
            if (CountChar(host, '.') == 3 && IPAddress.TryParse(host, out ip) &&
                ip.AddressFamily == AddressFamily.InterNetwork)
                return 0;

            if (host.IndexOf(':') >= 0 && IPAddress.TryParse(host, out ip) &&
                ip.AddressFamily == AddressFamily.InterNetworkV6)
                return 1;

            ip = null;
            return 2;
        }

        /// <summary>
        /// Splits "host:port". Bracketed IPv6 (<c>[::1]:22</c>) and bare IPv6 (<c>fe80::1</c>, which has more
        /// than one colon and no unambiguous port) are handled so the address is never cut in half.
        /// </summary>
        private static (string host, string port) SplitHostAndPort(string s)
        {
            if (s.StartsWith("[", StringComparison.Ordinal))
            {
                int close = s.IndexOf(']');
                if (close > 0)
                {
                    var host = s.Substring(1, close - 1);
                    var rest = s.Substring(close + 1);
                    var port = rest.StartsWith(":", StringComparison.Ordinal) ? rest.Substring(1) : "";
                    return (host, port);
                }
                return (s, "");
            }

            int first = s.IndexOf(':');
            if (first < 0)
                return (s, "");

            // More than one colon means a bare IPv6 literal: there is no port to peel off.
            if (s.IndexOf(':', first + 1) >= 0)
                return (s, "");

            return (s.Substring(0, first), s.Substring(first + 1));
        }

        private static int CompareBytes(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                if (a[i] != b[i])
                    return a[i] < b[i] ? -1 : 1;
            }
            return a.Length.CompareTo(b.Length);
        }

        private static int ComparePort(string a, string b)
        {
            bool ai = int.TryParse(a, out var pa);
            bool bi = int.TryParse(b, out var pb);
            if (ai && bi)
                return pa.CompareTo(pb);
            if (ai != bi)
                return ai ? 1 : -1; // a real port sorts after a missing/blank one
            return string.Compare(a, b, StringComparison.Ordinal);
        }

        /// <summary>
        /// Alphanumeric ("natural") comparison: contiguous digit runs are compared by magnitude rather than
        /// character by character, everything else case-insensitively. Digit runs are compared without
        /// converting to an integer, so an arbitrarily long number cannot overflow.
        /// </summary>
        private static int CompareNatural(string a, string b)
        {
            int ia = 0, ib = 0;
            while (ia < a.Length && ib < b.Length)
            {
                bool da = char.IsDigit(a[ia]);
                bool db = char.IsDigit(b[ib]);

                if (da && db)
                {
                    int sa = ia, sb = ib;
                    while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                    while (ib < b.Length && char.IsDigit(b[ib])) ib++;

                    int cmp = CompareDigitRun(a, sa, ia, b, sb, ib);
                    if (cmp != 0)
                        return cmp;
                }
                else
                {
                    int cmp = char.ToUpperInvariant(a[ia]).CompareTo(char.ToUpperInvariant(b[ib]));
                    if (cmp != 0)
                        return cmp;
                    ia++;
                    ib++;
                }
            }

            if (ia < a.Length) return 1;
            if (ib < b.Length) return -1;
            // Equal ignoring case: settle it ordinally so distinct strings never compare equal here.
            return string.Compare(a, b, StringComparison.Ordinal);
        }

        private static int CompareDigitRun(string a, int sa, int ea, string b, int sb, int eb)
        {
            // Skip leading zeros, then the longer remaining run is the larger number.
            while (sa < ea - 1 && a[sa] == '0') sa++;
            while (sb < eb - 1 && b[sb] == '0') sb++;

            int la = ea - sa, lb = eb - sb;
            if (la != lb)
                return la < lb ? -1 : 1;

            for (int i = 0; i < la; i++)
            {
                if (a[sa + i] != b[sb + i])
                    return a[sa + i] < b[sb + i] ? -1 : 1;
            }
            // Same magnitude (any difference is only in leading zeros): let the caller settle it on the
            // following chunks and, ultimately, an ordinal comparison, so "01" and "1" stay distinct.
            return 0;
        }

        private static int CountChar(string s, char c)
        {
            int n = 0;
            foreach (var ch in s)
                if (ch == c) n++;
            return n;
        }
    }
}
