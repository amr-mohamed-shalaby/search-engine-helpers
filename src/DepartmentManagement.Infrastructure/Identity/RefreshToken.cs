using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DepartmentManagement.Infrastructure.Identity
{
    /// <summary>
    /// Represents a refresh-token session.
    /// Only the SHA-256 token hash is stored in the database.
    /// </summary>
    public sealed class RefreshToken
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public Guid FamilyId { get; set; }

        public string TokenHash { get; set; } = string.Empty;

        public DateTimeOffset CreatedAtUtc { get; set; }

        public DateTimeOffset ExpiresAtUtc { get; set; }

        public DateTimeOffset? RevokedAtUtc { get; set; }

        public string? ReplacedByTokenHash { get; set; }

        public string? CreatedByIp { get; set; }

        public string? RevokedByIp { get; set; }

        public byte[] RowVersion { get; set; } = [];
    }
}