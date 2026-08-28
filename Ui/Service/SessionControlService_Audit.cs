using System;
using System.Collections.Concurrent;
using _1RM.Model.Protocol.Base;
using _1RM.Service.Audit;

namespace _1RM.Service
{
    public partial class SessionControlService
    {
        /// <summary>
        /// The attempt that is in flight or the session that is open, by connection id, so a close can be
        /// reported with the address the session actually went to and how long it lasted.
        ///
        /// It has to be captured here rather than read back off the protocol at close time: by then the
        /// clone has been redirected at a loopback port if a proxy was involved, and the audit line has to
        /// name the real host, not 127.0.0.1.
        /// </summary>
        private readonly ConcurrentDictionary<string, (ConnectionAuditRecord template, DateTime openedUtc)> _auditInFlight = new();

        private static ConnectionAuditLog? AuditLog => IoC.TryGet<ConnectionAuditLog>();

        /// <summary>
        /// Snapshots the identifying fields of a connection attempt. Never touches Password, PrivateKey or
        /// the external-secret command — see <see cref="ConnectionAuditRecord"/>.
        /// </summary>
        private static ConnectionAuditRecord BuildAuditRecord(ProtocolBase protocol, EAuditEvent auditEvent)
        {
            var record = new ConnectionAuditRecord
            {
                TimeUtc = DateTime.UtcNow,
                Event = auditEvent,
                Protocol = protocol.Protocol ?? "",
                ServerId = protocol.Id ?? "",
                ServerName = protocol.DisplayName ?? "",
                DataSource = protocol.DataSource?.DataSourceName ?? "",
                Proxy = protocol.ProxyName ?? "",
                ConnectionId = protocol.BuildConnectionId(),
            };

            if (protocol is ProtocolBaseWithAddressPort target)
            {
                record.Address = target.Address ?? "";
                record.Port = target.GetPort();
            }
            if (protocol is ProtocolBaseWithAddressPortUserPwd withUser)
            {
                record.RemoteUser = withUser.UserName ?? "";
            }

            return record;
        }

        private static ConnectionAuditRecord CopyOf(ConnectionAuditRecord template, EAuditEvent auditEvent)
        {
            return new ConnectionAuditRecord
            {
                TimeUtc = DateTime.UtcNow,
                Event = auditEvent,
                Protocol = template.Protocol,
                ServerId = template.ServerId,
                ServerName = template.ServerName,
                Address = template.Address,
                Port = template.Port,
                RemoteUser = template.RemoteUser,
                DataSource = template.DataSource,
                Proxy = template.Proxy,
                ConnectionId = template.ConnectionId,
                LocalUser = template.LocalUser,
                LocalMachine = template.LocalMachine,
            };
        }

        /// <summary>
        /// Called once the credentials are resolved and before the proxy rewrites the address, so the record
        /// names the host the user asked for.
        /// </summary>
        private void AuditConnectStarted(ProtocolBase protocol)
        {
            var record = BuildAuditRecord(protocol, EAuditEvent.ConnectStarted);
            _auditInFlight[record.ConnectionId] = (record, DateTime.UtcNow);
            AuditLog?.Record(record);
        }

        private void AuditConnectFailed(ProtocolBase protocol, string reason)
        {
            var connectionId = protocol.BuildConnectionId();
            var template = _auditInFlight.TryGetValue(connectionId, out var inFlight)
                ? inFlight.template
                : BuildAuditRecord(protocol, EAuditEvent.ConnectFailed);
            _auditInFlight.TryRemove(connectionId, out _);

            var record = CopyOf(template, EAuditEvent.ConnectFailed);
            record.Reason = reason;
            AuditLog?.Record(record);
        }

        private void AuditSessionOpened(string connectionId)
        {
            if (!_auditInFlight.TryGetValue(connectionId, out var inFlight)) return;
            _auditInFlight[connectionId] = (inFlight.template, DateTime.UtcNow);
            AuditLog?.Record(CopyOf(inFlight.template, EAuditEvent.SessionOpened));
        }

        private void AuditSessionClosed(string connectionId, string reason = "")
        {
            if (!_auditInFlight.TryRemove(connectionId, out var inFlight)) return;

            var record = CopyOf(inFlight.template, EAuditEvent.SessionClosed);
            record.Reason = reason;
            var seconds = (long)Math.Round((DateTime.UtcNow - inFlight.openedUtc).TotalSeconds);
            record.DurationSeconds = seconds > 0 ? seconds : 0;
            AuditLog?.Record(record);
        }
    }
}
