using System;
using _1RM.Model.Protocol.Base;

namespace _1RM.Service.Audit
{
    /// <summary>
    /// The five call sites that can send a credential out of the app, as one line each.
    ///
    /// Kept apart from <see cref="SecretAccessLog"/> so the log itself stays free of the app's model types
    /// and can be exercised without one, and so a caller that has a <see cref="ProtocolBase"/> in hand does
    /// not have to know which of its fields an audit record is allowed to hold.
    /// </summary>
    public static class SecretAccessAudit
    {
        private static SecretAccessLog? Log => IoC.TryGet<SecretAccessLog>();

        /// <summary>Fills in what an audit record may know about a server. Never a password or a key.</summary>
        private static SecretAccessRecord For(ESecretAccessEvent secretAccessEvent, ProtocolBase? server)
        {
            var record = new SecretAccessRecord
            {
                TimeUtc = DateTime.UtcNow,
                Event = secretAccessEvent,
            };

            if (server == null) return record;

            record.ServerId = server.Id ?? "";
            record.ServerName = server.DisplayName ?? "";
            record.Protocol = server.Protocol ?? "";
            record.DataSource = server.DataSource?.DataSourceName ?? "";
            if (server is ProtocolBaseWithAddressPort withAddress)
                record.Address = withAddress.Address ?? "";
            if (server is ProtocolBaseWithAddressPortUserPwd withUser)
                record.RemoteUser = withUser.UserName ?? "";

            return record;
        }

        public static void PasswordCopied(ProtocolBase server)
        {
            var record = For(ESecretAccessEvent.PasswordCopied, server);
            record.Destination = SecretAccessRecord.DESTINATION_CLIPBOARD;
            Log?.Record(record);
        }

        public static void ServerListExported(int serverCount, string path)
        {
            var record = For(ESecretAccessEvent.ServerListExported, null);
            record.Count = serverCount;
            record.Destination = path ?? "";
            // The one export that writes passwords as text. Worth saying so in the record itself, because
            // whoever reads the log later will not have the export dialog's warning in front of them.
            record.Note = "cleartext";
            Log?.Record(record);
        }

        public static void RdpFileExported(ProtocolBase server, string path)
        {
            var record = For(ESecretAccessEvent.RdpFileExported, server);
            record.Destination = path ?? "";
            Log?.Record(record);
        }

        public static void BackupCreated(string path, int entryCount, string note = "")
        {
            var record = For(ESecretAccessEvent.BackupCreated, null);
            record.Count = entryCount;
            record.Destination = path ?? "";
            record.Note = note;
            Log?.Record(record);
        }

        public static void AuditLogExported(int rowCount, string path)
        {
            var record = For(ESecretAccessEvent.AuditLogExported, null);
            record.Count = rowCount;
            record.Destination = path ?? "";
            Log?.Record(record);
        }
    }
}
