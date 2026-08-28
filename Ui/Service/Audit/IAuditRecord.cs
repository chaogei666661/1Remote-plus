using System;

namespace _1RM.Service.Audit
{
    /// <summary>
    /// What every audit record has to carry, whatever else it says.
    ///
    /// The two logs in this folder answer different questions — <see cref="ConnectionAuditRecord"/> is
    /// "who reached which host", <see cref="SecretAccessRecord"/> is "who took a credential out of the
    /// app" — but both are written to a day file by <see cref="AuditLogBase{T}"/>, which needs the
    /// timestamp to pick the file and fills in the operator when the caller did not.
    /// </summary>
    public interface IAuditRecord
    {
        /// <summary>UTC, so records from machines in different time zones sort and merge correctly.</summary>
        DateTime TimeUtc { get; set; }

        /// <summary>The Windows account that operated the app.</summary>
        string LocalUser { get; set; }

        /// <summary>The machine it was operated on.</summary>
        string LocalMachine { get; set; }
    }
}
