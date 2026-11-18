
using System.Security.Claims;
using DepartmentManagement.Contracts.Authentication;
using DepartmentManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace DepartmentManagement.Api.Authentication;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth")
            .WithTags("Authentication");

        group.MapPost("/login", async (
            LoginRequest request,
            HttpContext context,
            AuthenticationService service,
            CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Username) ||
                    string.IsNullOrEmpty(request.Password) ||
                    request.Username.Length > 256 ||
                    request.Password.Length > 1024)
                {
                    return Results.BadRequest(new
                    {
                        error = "Invalid username or password."
                    });
                }

                var response = await service.LoginAsync(
                    request,
                    context.Connection.RemoteIpAddress?.ToString(),
                    cancellationToken);

                return response is null
                    ? Results.Unauthorized()
                    : Results.Ok(response);
            })
        .AllowAnonymous();

        group.MapPost("/refresh", async (
            RefreshRequest request,
            HttpContext context,
            AuthenticationService service,
            CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.RefreshToken) ||
                    request.RefreshToken.Length > 256)
                {
                    return Results.BadRequest(new
                    {
                        error = "Invalid refresh token."
                    });
                }

                var response = await service.RefreshAsync(
                    request.RefreshToken,
                    context.Connection.RemoteIpAddress?.ToString(),
                    cancellationToken);

                return response is null
                    ? Results.Unauthorized()
                    : Results.Ok(response);
            })
        .AllowAnonymous();

        group.MapPost("/logout", async (
            LogoutRequest request,
            HttpContext context,
            AuthenticationService service,
            CancellationToken cancellationToken) =>
            {
                if (!string.IsNullOrWhiteSpace(request.RefreshToken) &&
                    request.RefreshToken.Length <= 256)
                {
                    await service.LogoutAsync(
                        request.RefreshToken,
                        context.Connection.RemoteIpAddress?.ToString(),
                        cancellationToken);
                }

                // Always return the same result, even for an unknown token.
                return Results.NoContent();
            })
        .AllowAnonymous();

        group.MapGet("/me", async (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager) =>
            {
                var userId = principal.FindFirstValue(
                    ClaimTypes.NameIdentifier);

                if (string.IsNullOrWhiteSpace(userId))
                    return Results.Unauthorized();

                var user = await userManager.FindByIdAsync(userId);

                if (user is null || !user.IsActive)
                    return Results.Unauthorized();

                var roles = await userManager.GetRolesAsync(user);

                return Results.Ok(new UserInfo(
                    user.Id,
                    user.UserName ?? string.Empty,
                    user.DisplayName,
                    roles.AsReadOnly()));
            })
        .RequireAuthorization();

        return endpoints;
    }
}
