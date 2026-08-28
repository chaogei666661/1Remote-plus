using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace _1RM.Service.Audit
{
    /// <summary>
    /// Renders credential-access records as CSV.
    ///
    /// Separate from the log so the escaping can be tested without touching a disk, and separate from
    /// <see cref="AuditCsv"/> because the columns are different: there is no port, no duration and no
    /// connection id here, and there is a destination and a count, which a connection never has. The
    /// escaping rules are the shared ones — a destination path a user chose can start with <c>=</c> just as
    /// easily as a server name can.
    /// </summary>
    public static class SecretAccessCsv
    {
        public const string Header = "utc_time,local_time,event,operator,operator_machine,server,protocol,address,remote_user,data_source,count,destination,note,server_id";

        public static string Write(IEnumerable<SecretAccessRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));

            var sb = new StringBuilder();
            sb.Append(Header).Append("\r\n");
            foreach (var r in records)
                sb.Append(Line(r)).Append("\r\n");
            return sb.ToString();
        }

        public static string Line(SecretAccessRecord r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            var fields = new[]
            {
                r.TimeUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                r.TimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                r.Event.ToString(),
                r.LocalUser,
                r.LocalMachine,
                r.ServerName,
                r.Protocol,
                r.Address,
                r.RemoteUser,
                r.DataSource,
                r.Count.ToString(CultureInfo.InvariantCulture),
                r.Destination,
                r.Note,
                r.ServerId,
            };

            var sb = new StringBuilder();
            for (var i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(AuditCsv.Escape(fields[i]));
            }
            return sb.ToString();
        }
    }
}
