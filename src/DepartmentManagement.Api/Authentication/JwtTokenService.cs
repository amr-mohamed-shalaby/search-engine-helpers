using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DepartmentManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DepartmentManagement.Api.Authentication;

public sealed class JwtTokenService
{
    private readonly JwtOptions _options;
    private readonly byte[] _signingKey;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.Issuer))
            throw new InvalidOperationException("JWT Issuer is missing.");

        if (string.IsNullOrWhiteSpace(_options.Audience))
            throw new InvalidOperationException("JWT Audience is missing.");

        if (string.IsNullOrWhiteSpace(_options.SigningKey))
            throw new InvalidOperationException("JWT SigningKey is missing.");

        if (_options.AccessTokenMinutes <= 0)
            throw new InvalidOperationException("JWT AccessTokenMinutes must be positive.");

        try
        {
            _signingKey = Convert.FromBase64String(_options.SigningKey);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "JWT SigningKey must be a Base64-encoded key.", ex);
        }

        if (_signingKey.Length < 32)
            throw new InvalidOperationException(
                "JWT SigningKey must contain at least 32 bytes.");
    }

    public (string Token, DateTimeOffset ExpiresAtUtc) CreateAccessToken(
        ApplicationUser user,
        IReadOnlyCollection<string> roles)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new("display_name", user.DisplayName)
        };

        claims.AddRange(roles.Select(role =>
            new Claim(ClaimTypes.Role, role)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(_signingKey),
            SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return (
            new JwtSecurityTokenHandler().WriteToken(jwt),
            expires);
    }
}