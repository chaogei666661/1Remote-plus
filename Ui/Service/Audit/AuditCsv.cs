using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace _1RM.Service.Audit
{
    /// <summary>
    /// Renders audit records as CSV.
    ///
    /// Separate from the log so the escaping can be tested without touching a disk, and because this is the
    /// format that leaves the machine: an exported audit log is opened in Excel by whoever asked for it.
    /// </summary>
    public static class AuditCsv
    {
        public const string Header = "utc_time,local_time,event,protocol,server,address,port,remote_user,data_source,proxy,reason,duration_seconds,operator,operator_machine,server_id,connection_id";

        public static string Write(IEnumerable<ConnectionAuditRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));

            var sb = new StringBuilder();
            sb.Append(Header).Append("\r\n");
            foreach (var r in records)
                sb.Append(Line(r)).Append("\r\n");
            return sb.ToString();
        }

        public static string Line(ConnectionAuditRecord r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            var fields = new[]
            {
                r.TimeUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                r.TimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                r.Event.ToString(),
                r.Protocol,
                r.ServerName,
                r.Address,
                r.Port.ToString(CultureInfo.InvariantCulture),
                r.RemoteUser,
                r.DataSource,
                r.Proxy,
                r.Reason,
                r.DurationSeconds.ToString(CultureInfo.InvariantCulture),
                r.LocalUser,
                r.LocalMachine,
                r.ServerId,
                r.ConnectionId,
            };

            var sb = new StringBuilder();
            for (var i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(fields[i]));
            }
            return sb.ToString();
        }

        /// <summary>
        /// RFC 4180 quoting, plus a guard against spreadsheet formula injection.
        ///
        /// Server names and user names come from whoever filled in the server list, which for a shared data
        /// source is not necessarily the person exporting. A field starting with =, +, - or @ is executed as
        /// a formula by Excel and LibreOffice when the file is opened, and DDE formulas can start a process.
        /// Prefixing an apostrophe makes the cell read as text; the value is still fully visible.
        /// </summary>
        public static string Escape(string? value)
        {
            var v = value ?? "";

            if (v.Length > 0 && "=+-@\t\r".IndexOf(v[0]) >= 0)
                v = "'" + v;

            if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                v = "\"" + v.Replace("\"", "\"\"") + "\"";

            return v;
        }
    }
}
