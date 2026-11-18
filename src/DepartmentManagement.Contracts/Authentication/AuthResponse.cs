namespace DepartmentManagement.Contracts.Authentication;

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    UserInfo User);

public sealed record UserInfo(
    Guid Id,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Roles);