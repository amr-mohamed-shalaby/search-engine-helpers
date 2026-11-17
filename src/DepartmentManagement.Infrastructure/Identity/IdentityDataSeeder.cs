using DepartmentManagement.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace DepartmentManagement.Infrastructure.Identity;

/// <summary>
/// Explicit, one-time provisioning of built-in roles and the first Admin.
/// This is never run during normal API startup.
/// </summary>
public static class IdentityDataSeeder
{
    public static async Task ProvisionAsync(IServiceProvider services)
    {
        // Validate all bootstrap inputs before changing the database.
        var username = RequiredEnvironmentVariable("INITIAL_ADMIN_USERNAME");
        var password = RequiredSecretEnvironmentVariable("INITIAL_ADMIN_PASSWORD");
        var displayName = RequiredEnvironmentVariable("INITIAL_ADMIN_DISPLAY_NAME");

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        var existingUser = await userManager.FindByNameAsync(username);
        if (existingUser is not null)
        {
            throw new InvalidOperationException(
                $"The account '{username}' already exists. No changes were made; investigate manually.");
        }

        await EnsureRoleAsync(roleManager, RoleNames.Admin);
        await EnsureRoleAsync(roleManager, RoleNames.Employee);

        var admin = new ApplicationUser
        {
            UserName = username,
            DisplayName = displayName,
            EmailConfirmed = false,
            LockoutEnabled = true
        };

        var createResult = await userManager.CreateAsync(admin, password);
        EnsureSucceeded(createResult, "creating the initial Admin account");

        var roleResult = await userManager.AddToRoleAsync(admin, RoleNames.Admin);
        if (!roleResult.Succeeded)
        {
            var deleteResult = await userManager.DeleteAsync(admin);
            var cleanup = deleteResult.Succeeded
                ? "The newly created account was rolled back."
                : $"WARNING: account cleanup also failed: {FormatErrors(deleteResult)}";

            throw new InvalidOperationException(
                $"Could not assign the Admin role: {FormatErrors(roleResult)}. {cleanup}");
        }

        Console.WriteLine($"Initial Admin account '{username}' was provisioned successfully.");
        Console.WriteLine("Remove INITIAL_ADMIN_USERNAME, INITIAL_ADMIN_PASSWORD, and INITIAL_ADMIN_DISPLAY_NAME from the process environment.");
    }

    private static async Task EnsureRoleAsync(
        RoleManager<IdentityRole<Guid>> roleManager,
        string roleName)
    {
        if (await roleManager.RoleExistsAsync(roleName))
            return;

        var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
        EnsureSucceeded(result, $"creating the '{roleName}' role");
    }

    private static string RequiredEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Required environment variable '{name}' is missing or empty.");

        return value.Trim();
    }

    private static string RequiredSecretEnvironmentVariable(string name)
    {
        // Do not trim passwords: leading/trailing spaces may be intentional.
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Required environment variable '{name}' is missing or empty.");

        return value;
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Identity operation failed while {operation}: {FormatErrors(result)}");
    }

    private static string FormatErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
}
