
using System.Data;
using DepartmentManagement.Contracts.Authentication;
using DepartmentManagement.Infrastructure.Identity;
using DepartmentManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DepartmentManagement.Api.Authentication;

public sealed class AuthenticationService(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    JwtTokenService jwtTokenService,
    IOptions<JwtOptions> jwtOptions)
{
    private readonly JwtOptions _options = jwtOptions.Value;

    public async Task<AuthResponse?> LoginAsync(
        LoginRequest request,
        string? clientIp,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByNameAsync(request.Username);

        if (user is null)
            return null;

        if (await userManager.IsLockedOutAsync(user))
            return null;

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            if (user.LockoutEnabled)
                await userManager.AccessFailedAsync(user);

            return null;
        }

        await userManager.ResetAccessFailedCountAsync(user);

        return await CreateSessionAsync(
            user,
            Guid.NewGuid(),
            clientIp,
            cancellationToken);
    }

    public async Task<AuthResponse?> RefreshAsync(
        string rawRefreshToken,
        string? clientIp,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            return null;

        var tokenHash = RefreshTokenGenerator.Hash(rawRefreshToken);
        var now = DateTimeOffset.UtcNow;

        await using var transaction =
            await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var existing = await db.RefreshTokens
            .SingleOrDefaultAsync(
                token => token.TokenHash == tokenHash,
                cancellationToken);

        if (existing is null)
            return null;

        // Reuse of a revoked token means its session family is compromised.
        if (existing.RevokedAtUtc is not null)
        {
            await RevokeFamilyAsync(
                existing.FamilyId,
                clientIp,
                now,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (existing.ExpiresAtUtc <= now)
        {
            existing.RevokedAtUtc = now;
            existing.RevokedByIp = clientIp;

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var user = await userManager.FindByIdAsync(
            existing.UserId.ToString());

        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            await RevokeFamilyAsync(
                existing.FamilyId,
                clientIp,
                now,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var replacementRawToken = RefreshTokenGenerator.Generate();
        var replacementHash = RefreshTokenGenerator.Hash(
            replacementRawToken);

        var replacementExpiry = now.AddDays(_options.RefreshTokenDays);

        existing.RevokedAtUtc = now;
        existing.RevokedByIp = clientIp;
        existing.ReplacedByTokenHash = replacementHash;

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FamilyId = existing.FamilyId,
            TokenHash = replacementHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = replacementExpiry,
            CreatedByIp = clientIp
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var roles = await userManager.GetRolesAsync(user);
        var accessToken = jwtTokenService.CreateAccessToken(user, roles.AsReadOnly());

        return new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            replacementRawToken,
            replacementExpiry,
            new UserInfo(
                user.Id,
                user.UserName ?? string.Empty,
                user.DisplayName,
                roles.AsReadOnly()));
    }

    public async Task LogoutAsync(
        string rawRefreshToken,
        string? clientIp,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            return;

        var tokenHash = RefreshTokenGenerator.Hash(rawRefreshToken);

        var token = await db.RefreshTokens
            .SingleOrDefaultAsync(
                item => item.TokenHash == tokenHash,
                cancellationToken);

        if (token is null)
            return;

        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        await RevokeFamilyAsync(
            token.FamilyId,
            clientIp,
            DateTimeOffset.UtcNow,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<AuthResponse> CreateSessionAsync(
        ApplicationUser user,
        Guid familyId,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var refreshToken = RefreshTokenGenerator.Generate();
        var refreshExpiry = now.AddDays(_options.RefreshTokenDays);

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FamilyId = familyId,
            TokenHash = RefreshTokenGenerator.Hash(refreshToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshExpiry,
            CreatedByIp = clientIp
        });

        await db.SaveChangesAsync(cancellationToken);

        var roles = await userManager.GetRolesAsync(user);
        var accessToken = jwtTokenService.CreateAccessToken(user, roles.AsReadOnly());

        return new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            refreshToken,
            refreshExpiry,
            new UserInfo(
                user.Id,
                user.UserName ?? string.Empty,
                user.DisplayName,
                roles.AsReadOnly()));
    }

    private async Task RevokeFamilyAsync(
        Guid familyId,
        string? clientIp,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        await db.RefreshTokens
            .Where(token =>
                token.FamilyId == familyId &&
                token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        token => token.RevokedAtUtc,
                        revokedAt)
                    .SetProperty(
                        token => token.RevokedByIp,
                        clientIp),
                cancellationToken);
    }
}
