using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DepartmentManagement.Api.Authentication
{
    public sealed class JwtOptions
    {
        public const string SectionName = "Jwt";

        public string Issuer { get; set; } = string.Empty;

        public string Audience { get; set; } = string.Empty;

        public string SigningKey { get; set; } = string.Empty;

        public int AccessTokenMinutes { get; set; } = 15;

        public int RefreshTokenDays { get; set; } = 7;
    }
}