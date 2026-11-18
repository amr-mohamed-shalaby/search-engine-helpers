namespace DepartmentManagement.Contracts.Authentication;

public sealed record LogoutRequest(
    string RefreshToken);
